// ERROR: use of undeclared identifier 'None'
// AT: None
const farm = @import("farm");

pub fn main() void {
    if (farm.getEntityType() == None) {
        _ = farm.plant(.Grass);
    }
}
