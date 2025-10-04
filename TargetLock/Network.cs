using CircularBuffer;
using MessagePack;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace TargetLock;

public class Network
{
    [MessagePackObject]
    public struct Position
    {
        [Key(0)]
        public int x;
        [Key(1)]
        public int y;
    }

    [MessagePackObject]
    public struct Data
    {
        [Key(1)]
        public DateTime currentTime;

        [Key(3)]
        public Position current;

        [Key(4)]
        public double vx;
        [Key(5)]
        public double vy;

        // Last 10 Moves
        [Key(0)]
        public DateTime[] lastTimes;
        [Key(6)]
        public Position[] lastPostitons;
    }

    public static bool DataCapture = false;
    public List<Data> Datas = new();

    public CircularBuffer<DateTime> lastTimes = new(10);
    public CircularBuffer<Position> lastPositions = new(10);

    public int count = 0;
    
    public void StartDataCapture()
    {
        DataCapture = true;
    }

    public void Save()
    {
        byte[] bytes = MessagePackSerializer.Serialize(Datas);
        File.WriteAllBytes("./datasets/data-0.bin", bytes);
    }

    public PositionPredictor InfrencePredictor = new(30, dModel: 512, nHead: 32, 1);
    public void StartInference()
    {
        InfrencePredictor.load(@"D:\Programming\Projects\Personal\TargetLock\publish\datasets\models\best_model.pt").to(CUDA);
        InfrencePredictor.eval();
    }

    public (int x, int y) Infer(Data data)
    {
        using var input = DataToTensor(data).unsqueeze(0).to(CUDA);

        using var test = InfrencePredictor.Forward(input);
        
        var prediction = test.cpu().data<float>().ToArray();

        return ((int)prediction[0], (int)prediction[1]);
    }

    public void Train()
    {
        byte[] loadedBytes = File.ReadAllBytes("./datasets/spectator.bin");
        byte[] loadedBytes2 = File.ReadAllBytes("./datasets/original.bin");
        byte[] loadedBytes3 = File.ReadAllBytes("./datasets/original-2.bin");
        byte[] loadedBytes4 = File.ReadAllBytes("./datasets/original-3.bin");
        byte[] loadedBytes5 = File.ReadAllBytes("./datasets/original-4.bin");
        byte[] loadedBytes6 = File.ReadAllBytes("./datasets/original-5.bin");

        List<Data> sequence = MessagePackSerializer.Deserialize<List<Data>>(loadedBytes);
        List<Data> sequence2 = MessagePackSerializer.Deserialize<List<Data>>(loadedBytes2);
        List<Data> sequence3 = MessagePackSerializer.Deserialize<List<Data>>(loadedBytes3);
        List<Data> sequence4 = MessagePackSerializer.Deserialize<List<Data>>(loadedBytes4);
        List<Data> sequence5 = MessagePackSerializer.Deserialize<List<Data>>(loadedBytes5);
        List<Data> sequence6 = MessagePackSerializer.Deserialize<List<Data>>(loadedBytes6);

        sequence.AddRange(sequence2);
        sequence.AddRange(sequence3);
        sequence.AddRange(sequence4);
        sequence.AddRange(sequence5);
        sequence.AddRange(sequence6);

        var device = cuda.is_available() ? CUDA : CPU;
        
        var inputs = new List<Tensor>();
        var targets = new List<Tensor>();

        // Prediction Window
        var pWindow = 3;
        for (int i = 0; i < (sequence.Count - pWindow); i++)
        {
            // TODO: Checks if data[~0] && data[~1] is related in time, if not scrap as outlier
            var timeMsDelta = (sequence[i + pWindow].currentTime.Ticks * 100.0 - sequence[i].currentTime.Ticks * 100.0) /
                              1e+6;
            
            // TODO: Here we're assuming FPS is 175
            if (timeMsDelta > ((1000.0 / 175.0) * pWindow) + 1)
            {
                continue;
            }

            if (sequence[i].lastPostitons.Length < 10)
            {
                continue;
            }
            
            var input = DataToTensor(sequence[i]);
            // var target = PositionToTargetTensor(sequence[i + pWindow].current);
            var target = PositionToTargetTensor(sequence[i + pWindow].current, sequence[i].current);

            inputs.Add(input);
            targets.Add(target);
        }
        
        var inputBatch = stack(inputs.ToArray());
        var targetBatch = stack(targets.ToArray());
        
        Console.WriteLine($"Running with: {inputs.Count} data points");
        Console.WriteLine($"Size: {inputBatch.shape[1]}");
        
        var model = new PositionPredictor(inputBatch.shape[1], dModel: 512, nHead: 32, 1);
        model.to(device);

        // Smaller dataset is present, should probably use NAdam or RAdam, but that's just my thoughts.
        // var optimizer = optim.RAdam(model.parameters(), lr: 0.002);
        var optimizer = optim.AdamW(model.parameters());
        
        var lossFunc = MSELoss();
        
        int epochs = 10000;
    
        var inputsDevice = inputBatch.to(device);
        var targetsDevice = targetBatch.to(device);
        
        model.train();
        
        for (int epoch = 0; epoch < epochs; epoch++)
        {
            optimizer.zero_grad();
            
            using var predictions = model.Forward(inputsDevice);
            
            using var loss = lossFunc.forward(predictions, targetsDevice);
            loss.backward();
            nn.utils.clip_grad_norm_(model.parameters(), max_norm: 1.0);
            using var step = optimizer.step();
            
            Console.WriteLine($"Epoch {epoch+1}, Loss: {loss.item<float>()}");
        }
        
        model.eval();
        
        var inputtest = DataToTensor(sequence[101]).unsqueeze(0).to(device);

        var test = model.Forward(inputtest); 
        
        var prediction = test.cpu().data<float>().ToArray();

        Console.WriteLine($"Predicted: [{prediction[0]}, {prediction[1]}], Truth: [{sequence[101 + pWindow].current.x}, {sequence[101 + pWindow].current.y}]");

        model.save(@"D:\\Programming\\Projects\\Personal\\TargetLock\\publish\\datasets\\models\best_model.pt");
    }
    
    public sealed class PositionPredictor : Module
    {
        private readonly Linear inputProjection;
        private readonly TransformerEncoder encoder;
        private readonly Linear fc;


        public PositionPredictor(long inputSize, int dModel, int nHead, int numLayers, int dimFeedforward = 256, double dropout = 0.1)
            : base("Prediction Network")
        {
            inputProjection = Linear(inputSize, dModel);
            
            var encoderLayer  = TransformerEncoderLayer(
                d_model: dModel,
                nhead: nHead,
                dim_feedforward: dimFeedforward,
                dropout: dropout,
                activation: Activations.GELU
            );
            encoder = TransformerEncoder(encoderLayer, numLayers);

            fc = Linear(dModel, 2); // Output: x and y
            
            RegisterComponents();
        }
        
        public Tensor Forward(Tensor input)
        {
            // // input: (batch_size, inputSize)
            var batchSize = input.shape[0];
            using var x1 = inputProjection.forward(input);               // (batch_size, dModel)
            using var x2 = x1.view(1, batchSize, -1);                         // (sequence_length=1, batch_size, dModel)
            
            using var encoded = encoder.forward(x2, null, null);                     // (1, batch_size, dModel)
            using var output = encoded[-1];                              // (batch_size, dModel)
            
            return fc.forward(output);                            // (batch_size, 2)
        }
    }
    
    public static double[] FlattenData(Data data)
    {
        var list = new List<double>
        {
            // Positions
            data.lastPostitons[^10].x - data.lastPostitons[^9].x,
            data.lastPostitons[^10].y - data.lastPostitons[^9].y,
            data.lastPostitons[^9].x - data.lastPostitons[^8].x,
            data.lastPostitons[^9].y - data.lastPostitons[^8].y,
            data.lastPostitons[^8].x - data.lastPostitons[^7].x,
            data.lastPostitons[^8].y - data.lastPostitons[^7].y,
            data.lastPostitons[^7].x - data.lastPostitons[^6].x,
            data.lastPostitons[^7].y - data.lastPostitons[^6].y,
            data.lastPostitons[^6].x - data.lastPostitons[^5].x,
            data.lastPostitons[^6].y - data.lastPostitons[^5].y,
            
            data.lastPostitons[^5].x - data.lastPostitons[^4].x,
            data.lastPostitons[^5].y - data.lastPostitons[^4].y,
            data.lastPostitons[^4].x - data.lastPostitons[^3].x,
            data.lastPostitons[^4].y - data.lastPostitons[^3].y,
            data.lastPostitons[^3].x - data.lastPostitons[^2].x,
            data.lastPostitons[^3].y - data.lastPostitons[^2].y,
            data.lastPostitons[^2].x - data.lastPostitons[^1].x,
            data.lastPostitons[^2].y - data.lastPostitons[^2].y,
            // data.lastPostitons[^1].x - data.current.x,
            // data.lastPostitons[^1].y - data.current.y,
            data.lastPostitons[^5].x,
            data.lastPostitons[^5].y,
            data.lastPostitons[^4].x,
            data.lastPostitons[^4].y,
            data.lastPostitons[^3].x,
            data.lastPostitons[^3].y,
            data.lastPostitons[^2].x,
            data.lastPostitons[^2].y,
            data.lastPostitons[^1].x,
            data.lastPostitons[^1].y,
            data.current.x,
            data.current.y,
            // Velocities
            // data.vx,
            // data.vy,
            // data.currentTime.Ticks * 100.0
        };

        return list.ToArray();
    }
    
    public static Tensor DataToTensor(Data data)
    {
        double[] flat = FlattenData(data);
        return tensor(flat, float32);
    }
    
    // public static Tensor PositionToTargetTensor(Position pos)
    // {
    //     return tensor(new double[] { pos.x, pos.y }, dtype: float32);
    // }
    
    public static Tensor PositionToTargetTensor(Position futurePos, Position currentPos)
    {
        var dx = futurePos.x - currentPos.x;
        var dy = futurePos.y - currentPos.y;

        return tensor(new float[] { dx, dy }, dtype: float32);
    }
}