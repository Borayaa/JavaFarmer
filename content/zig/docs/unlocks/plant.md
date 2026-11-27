# Plant
Grass is nice because it grows automatically. All other plants have to be planted with the `farm.plant()` function. The only plant you can plant right now is a bush.
You can pass the type of plant you want to plant to the function like this:

`_ = farm.plant(.Bush);`

This will plant a bush under the drone. `.Bush` is short for `farm.Entity.Bush`. `farm.plant()` returns `true` if it worked; `_ =` discards that result.

Call `farm.clear();` to reset the farm to all grass and reset the drone position.

It seems that if you grow more than one type of plant on the farm at the same time, you can sometimes get a higher yield. You'll need to research polyculture to learn more.