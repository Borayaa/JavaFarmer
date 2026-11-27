// ERROR: expected statement, found 'a document comment'
// AT: /// moves once
const farm = @import("farm");

pub fn main() void {
    /// moves once
    _ = farm.move(.North);
}
