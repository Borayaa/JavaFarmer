//! while with a continue expression, for over ranges and arrays, break and continue.
const std = @import("std");

pub fn main() void {
    var i: i32 = 0;
    var odd_sum: i32 = 0;
    while (i < 10) : (i += 1) {
        if (@mod(i, 2) == 0) continue;
        odd_sum += i;
    }
    std.debug.print("odd sum {d}, i {d}\n", .{ odd_sum, i });
    var total: usize = 0;
    for (0..5) |a| {
        for (a..5) |b| {
            total += a * b;
        }
    }
    std.debug.print("total {d}\n", .{total});
    var n: i32 = 27;
    var steps: i32 = 0;
    while (n != 1) {
        if (@mod(n, 2) == 0) {
            n = @divTrunc(n, 2);
        } else {
            n = 3 * n + 1;
        }
        steps += 1;
    }
    std.debug.print("collatz 27: {d} steps\n", .{steps});
    const primes = [_]i32{ 2, 3, 5, 7, 11, 13 };
    var first_big: i32 = -1;
    for (primes) |p| {
        if (p > 6) {
            first_big = p;
            break;
        }
    }
    std.debug.print("first prime above 6: {d}\n", .{first_big});
    var count: i32 = 0;
    while (true) {
        count += 3;
        if (count > 10) break;
    }
    std.debug.print("count {d}\n", .{count});
}
