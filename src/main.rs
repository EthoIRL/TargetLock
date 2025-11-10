use hyprland_preview_share_picker_lib::image::{Image, ImageKind};
use hyprland_preview_share_picker_lib::output::OutputManager;
use std::net::{Ipv4Addr, SocketAddrV4, UdpSocket};
use wayland_client::Connection;

const WIDTH: i32 = 300;
const HEIGHT: i32 = 300;

/// NOTE: BattleBit Specific:
/// Performs color calculations every 5y pixels.
/// This saves around 80% of the calculation while maintaining full accurate search.
/// 300 Lines -> 60 Lines (-80%)
/// 
/// OTHER: Depending on a game's color indicator this can be larger/smaller or disabled.
const HEIGHT_SKIP: usize = 5;

const KP: f32 = 0.7;
const KD: f32 = 0.3;

fn main() {
    let socket = UdpSocket::bind("0.0.0.0:0").expect("Failed to bind to local socket!");
    let connection = Connection::connect_to_env().expect("Failed to connect to a wayland session!");

    let mut output_manager = OutputManager::new(&connection).expect("Failed to create output capturing manager!");
    let (wl_output, display) = output_manager.outputs.first().expect("Failed to get a display!").clone();

    println!("{:#?}", wl_output);
    println!("{:#?}", display);

    let broadcast_addr = Ipv4Addr::new(192, 168, 68, 68);
    let endpoint = SocketAddrV4::new(broadcast_addr, 7483);

    let display_mode = display.mode.expect("Failed to get display mode & geometry!");

    let display_center_x = display_mode.width / 2;
    let display_center_y = display_mode.height / 2;

    let mut last_error_x: f32 = 0.0;
    let mut last_error_y: f32 = 0.0;

    loop {
        let buffer = output_manager.capture_output_region(&wl_output, display_center_x - WIDTH / 2, display_center_y - HEIGHT / 2, WIDTH, HEIGHT).unwrap();

        let xbgr_image = match Image::new(buffer).unwrap().buffer {
            ImageKind::Xrgb(image_buffer) => image_buffer,
            ImageKind::Rgb(_) => unreachable!("rgb"),
        };

        let mut found = false;

        for y in (0..HEIGHT).rev().step_by(HEIGHT_SKIP) {
            if found {
                break;
            }
        
            for x in 0..WIDTH {
                if found {
                    break;
                }
                
                let pixel = xbgr_image.get_pixel(x as u32, y as u32).0;

                if is_pink(&pixel) {
                    for local_y in (0..HEIGHT_SKIP).rev() {
                        let y = y + local_y as i32;
                        
                        if y > HEIGHT {
                            continue;
                        } 
                        
                        let local_pixel = xbgr_image.get_pixel(x as u32, y as u32).0;
        
                        if is_pink(&local_pixel) {
                            handle_movement(x, y, &mut last_error_x, &mut last_error_y, &socket, &endpoint);
        
                            found = true;
                            break;
                        }
                    }
                }
            }
        }
    }
}

fn handle_movement(offset_x: i32, offset_y: i32, last_error_x: &mut f32, last_error_y: &mut f32, socket: &UdpSocket, endpoint: &SocketAddrV4) {
    let delta_x = (WIDTH as f32 / 2.0) - offset_x as f32;
    let delta_y = (HEIGHT as f32 / 2.0) - offset_y as f32 - 1.0;

    let d_error_x = delta_x - *last_error_x;
    let d_error_y = delta_y - *last_error_y;

    let move_x = KP * delta_x + KD * d_error_x;
    let move_y = KP * delta_y + KD * d_error_y;

    let packet = prepare_packet(-f32::round(move_x) as i16, -f32::round(move_y) as i16);
    socket.send_to(&packet, endpoint).unwrap();

    *last_error_x = delta_x;
    *last_error_y = delta_y;
}

/// Assumes BGRX format
fn is_pink(pixel: &[u8; 4]) -> bool {
    pixel[0] > 200 && pixel[2] > 180 && pixel[1] < 100
}

fn prepare_packet(delta_x: i16, delta_y: i16) -> [u8; 4] {
    [
        (delta_x & 0xFF) as u8,
        ((delta_x >> 8) & 0xFF) as u8,
        (delta_y & 0xFF) as u8,
        ((delta_y >> 8) & 0xFF) as u8,
    ]
}
