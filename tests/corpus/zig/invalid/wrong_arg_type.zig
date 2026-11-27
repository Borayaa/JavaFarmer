// ERROR: expected type 'farm.Entity', found 'farm.Item'
// AT: farm.Item.Hay
const farm = @import("farm");

pub fn main() void {
    _ = farm.plant(farm.Item.Hay);
}
