// Option values from the game: if let, match, unwrap_or, is_some.
use farm::*;

fn describe(e: Option<Entity>) -> String {
    match e {
        Some(kind) => format!("found {}", kind),
        None => String::from("empty"),
    }
}

fn main() {
    let here = get_entity_type();
    println!("{}", describe(here));
    if here.is_some() && here != Some(Entity::Grass) {
        harvest();
    }
    let kind = here.unwrap_or(Entity::Grass);
    plant(kind);
    match get_entity_type() {
        Some(Entity::Carrot) | Some(Entity::Pumpkin) => quick_print!("crop"),
        Some(Entity::Tree) => quick_print!("tree"),
        _ => quick_print!("other"),
    }
}
