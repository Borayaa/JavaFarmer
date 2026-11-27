// Zig does not allow a local to shadow another one.
// ERROR: local constant 'x' shadows local constant from outer scope
// AT: x
const farm = @import("farm");

pub fn main() void {
    const x = farm.getPosX();
    if (x > 2) {
        const x = 1;
        _ = x;
    }
}
