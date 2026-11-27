// getEntityType() returns ?farm.Entity: unwrap it first (if (x) |e|, orelse, .?).
// ERROR: operator == not allowed for type '?farm.Entity'
// AT: farm.getEntityType() == .Carrot
const farm = @import("farm");

pub fn main() void {
    if (farm.getEntityType() == .Carrot) {
        _ = farm.harvest();
    }
}
