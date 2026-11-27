# Mazes
`Item::Weird_Substance`, which is obtained by [fertilizing](docs/unlocks/fertilizer.md) plants, has a strange effect on bushes. If the drone is over a bush and you call `use_item_with(Item::Weird_Substance, amount);` the bush will grow into a maze of hedges.
The size of the maze depends on the amount of `Item::Weird_Substance` used (the second argument of the `use_item_with()` call).
Without maze upgrades, using `n` `Item::Weird_Substance` will result in a `n`x`n` maze. Each maze upgrade level doubles the treasure, but it also doubles the amount of `Item::Weird_Substance` needed. 
So to make a full field maze:

`plant(Entity::Bush);
let substance = get_world_size() * 2_i32.pow((num_unlocked(Unlock::Mazes) - 1) as u32);
use_item_with(Item::Weird_Substance, substance);`

Rust has no power operator. `2_i32.pow(k)` is `2` to the power of `k`, and the exponent must be a `u32`, so the `i32` from `num_unlocked()` is converted with `as u32`.

For some reason the drone can't fly over the hedges, even though they don't look that high.

There is a treasure hidden somewhere in the hedge. Use `harvest()` on the treasure to receive gold equal to the area of the maze. (For example, a 5x5 maze will yield 25 gold.)

If you use `harvest()` anywhere else the maze will simply disappear.

`get_entity_type()` is equal to `Some(Entity::Treasure)` if the drone is over the treasure and `Some(Entity::Hedge)` everywhere else in the maze.

Mazes do not contain any loops unless you reuse the maze (see below how to reuse a maze). So there is no way for the drone to end up in the same position again without going back.

You can check if there is a wall by trying to move through it. 
`move_to()` returns `true` if it succeeded and `false` otherwise.

`can_move()` can be used to check if there is a wall without moving.

If you have no idea how to get to the treasure, take a look at Hint 1. It shows you how to approach a problem like this.

Using `measure()` anywhere in the maze returns the `Position` of the treasure. `measure()` can return different types, so tell Rust that you want a `Position` (a tuple `(i32, i32)`) and take it apart into `x` and `y`:
`let (x, y): Position = measure();`

For an extra challenge you can also reuse the maze by using the same amount of `Item::Weird_Substance` on the treasure again. 
This will increase the amount of gold in the treasure by one full maze and move it to a random position in the maze.

Each time the treasure is moved, some of the maze's walls may be randomly removed. So reused mazes can contain loops.

Note that loops in the maze make it much more difficult because it means that you can get to the same location again without moving back.
Reusing a maze doesn't give you more gold than just harvesting and spawning a new maze.
This is 100% an extra challenge that you can just skip.
It's only worth it if the extra information and the shortcuts help you solve the maze faster.

The treasure can be relocated up to 300 times. After that, using weird substance on the treasure won't increase the gold in it anymore and it won't move anymore.

<spoiler=show hint 1>Here's a general approach to solving the problem:

Create a maze and imagine that you are the drone.

Think about how you would try to find the treasure if you were in the maze.

Write down your strategy step by step so that someone else could follow it without thinking.

Now try translating your steps into code.
</spoiler>
<spoiler=show hint 2>As long as there are no loops: All the walls are really just one large connected wall. If you follow the wall, it will lead you through the whole maze.
This approach requires very little code and you do not need to keep track of where you have already been. Around 10 lines of code is all you need.</spoiler>
<spoiler=show hint 3>Instead of moving the drone in absolute directions like east or west it can be very useful to move the drone in relative directions like "turn right" or "turn left". To do this you need to keep track of which way the drone is currently moving. The drone never actually rotates, but you can still keep a "virtual" rotation in code.
The following index trick is helpful for this:

`let directions = [North, East, South, West];
let mut index: usize = 0;`

The index is a `usize` because Rust only indexes arrays and `Vec`s with `usize`. It must be `let mut`, because it changes.
Use `% 4` to allow it to rotate "around the circle", so that after `West` it wraps back to `North`.
`// turn right
index = (index + 1) % 4;`

To turn left, add `3` instead of subtracting `1`. A `usize` can never be negative, so `index - 1` would be an error when `index` is `0`.
`// turn left
index = (index + 3) % 4;

move_to(directions[index]);`</spoiler>
<spoiler=show hint 4>If you can't solve it, you can always make your life easy and do it less efficiently. 
Solving a `1`x`1` maze is trivial.</spoiler>
