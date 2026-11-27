// ERROR: function with non-void return type 'i32' implicitly returns
// AT: }
const farm = @import("farm");

fn size() i32 {
    const n: i32 = @intCast(farm.getWorldSize());
    if (n > 3) {
        return n;
    }
}

pub fn main() void {
    _ = size();
}
