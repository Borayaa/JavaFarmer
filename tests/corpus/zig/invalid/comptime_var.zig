// var x = 0 has the type comptime_int: give it a type.
// ERROR: variable of type 'comptime_int' must be const or comptime
// AT: 0
const farm = @import("farm");

pub fn main() void {
    var i = 0;
    while (i < 10) : (i += 1) {
        _ = farm.move(.North);
    }
}
