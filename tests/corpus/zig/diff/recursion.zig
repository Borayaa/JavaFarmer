//! Recursive functions: factorial, fibonacci, gcd and the towers of Hanoi.
const std = @import("std");

fn factorial(n: i64) i64 {
    if (n <= 1) return 1;
    return n * factorial(n - 1);
}

fn fib(n: i32) i32 {
    if (n < 2) {
        return n;
    }
    return fib(n - 1) + fib(n - 2);
}

fn gcd(a: i32, b: i32) i32 {
    if (b == 0) return a;
    return gcd(b, @mod(a, b));
}

fn hanoi(n: i32) i32 {
    if (n == 0) return 0;
    return 2 * hanoi(n - 1) + 1;
}

pub fn main() void {
    std.debug.print("10! = {d}\n", .{factorial(10)});
    std.debug.print("fib(15) = {d}\n", .{fib(15)});
    std.debug.print("gcd(84, 36) = {d}\n", .{gcd(84, 36)});
    std.debug.print("hanoi(10) = {d}\n", .{hanoi(10)});
}
