//! Bubble sort, a prime sieve and binary search on arrays.
const std = @import("std");

fn find(sorted: []const i32, target: i32) i32 {
    var lo: usize = 0;
    var hi: usize = sorted.len;
    while (lo < hi) {
        const mid = (lo + hi) / 2;
        if (sorted[mid] == target) {
            return @intCast(mid);
        }
        if (sorted[mid] < target) {
            lo = mid + 1;
        } else {
            hi = mid;
        }
    }
    return -1;
}

pub fn main() void {
    var data = [_]i32{ 5, 3, 9, 1, 7, 2 };
    var swapped = true;
    while (swapped) {
        swapped = false;
        for (0..data.len - 1) |i| {
            if (data[i] > data[i + 1]) {
                const tmp = data[i];
                data[i] = data[i + 1];
                data[i + 1] = tmp;
                swapped = true;
            }
        }
    }
    std.debug.print("{any}\n", .{data});
    var composite: [30]bool = @splat(false);
    var count: i32 = 0;
    for (2..30) |n| {
        if (composite[n]) continue;
        count += 1;
        var m = n * n;
        while (m < 30) : (m += n) {
            composite[m] = true;
        }
    }
    std.debug.print("{d} primes below 30\n", .{count});
    std.debug.print("{d} {d}\n", .{ find(&data, 7), find(&data, 4) });
}
