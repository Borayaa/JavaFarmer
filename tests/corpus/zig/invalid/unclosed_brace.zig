// ERROR: expected '}', found 'EOF'
const farm = @import("farm");

pub fn main() void {
    while (true) {
        _ = farm.harvest();
