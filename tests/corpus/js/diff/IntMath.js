// Numbers are doubles: / is real division, % keeps the dividend's sign, Math.floor/trunc for integers.
console.log(7 / 2, -7 / 2);
console.log(7 % 3, -7 % 3, 7 % -3, -7 % -3);
console.log(Math.floor(7 / 2), Math.floor(-7 / 2), Math.trunc(-7 / 2), Math.ceil(7 / 2));
console.log(Math.round(2.5), Math.round(-2.5), Math.round(2.4));
console.log(2 ** 10, Math.pow(3, 3), Math.sqrt(81));
console.log(Math.min(4, 2, 8), Math.max(4, 2, 8), Math.abs(-6));
let x = 17;
x %= 5;
console.log(x);
x **= 3;
console.log(x);
x /= 4;
console.log(x);
let y = -9;
y %= 4;
console.log(y);
console.log(0.1 + 0.2, 1 / 3, 1e3, 2.5e-3, 1_000_000);
console.log(0xff, 0b101, 0o17);
