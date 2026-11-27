// farm.harvest() returns a bool: ignore it with _ = ...
// ERROR: value of type 'bool' ignored
// AT: farm.harvest()
const farm = @import("farm");

pub fn main() void {
    farm.harvest();
}
