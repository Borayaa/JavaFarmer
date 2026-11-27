// ERROR: identifiers starting with '__j' are reserved by JavaFarmer
// AT: __jcount
const farm = @import("farm");

pub fn main() void {
    const __jcount = farm.getWorldSize();
    _ = __jcount;
}
