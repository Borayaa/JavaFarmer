# Expand 2
Your farm has expanded again! Now the tiles are no longer in a nice row, so you need to find a way to traverse a square grid.

With the `while` loop this is not possible until you unlock senses and operators.
It is time to introduce the `for` loop.

You can read all about the `for` loop on the [For Loop](docs/scripting/for.md) page, but for now you will only need it to repeat code a fixed number of times.

`// do 5 flips
for (0..5) |_| {
    farm.doAFlip();
}`

`0..5` is a range of numbers from `0` to `4`: it has `5` elements in it (the end is not included). The `for` loop runs it's loop body once for every element in the range. Between the `|` bars you can name the current number (`|i|`); `|_|` means you don't need it. In this example `farm.doAFlip()` will be called `5` times.

The function `farm.getWorldSize()` is also available now. It returns the side length of your farm. This way you can write code that won't break with the next expand upgrade.

`for (0..farm.getWorldSize()) |_| {
    _ = farm.harvest();
    _ = farm.move(.North);
}`

This example harvests one column of the farm for any farm size.

If you're stuck on trying to figure out how to move the drone around the farm see the hint below.
<spoiler=show hint>There are, of course, several ways to move around the farm.
What we're looking for is a way to traverse it in a systematic way that won't break when the farm grows again.
A systematic way to get to every place on the farm would be to repeat the following 2 steps forever:

1.Move `.North` until it wraps back.
2.Move `.East`

`for (0..farm.getWorldSize()) |_|` may be helpful to turn this idea into code.
</spoiler>
<spoiler=show possible solution> The basic traversal might look like this:

`for (0..farm.getWorldSize()) |_| {
    for (0..farm.getWorldSize()) |_| {
        // do a flip on every tile
        farm.doAFlip();
        _ = farm.move(.North);
    }
    _ = farm.move(.East);
}`
</spoiler>