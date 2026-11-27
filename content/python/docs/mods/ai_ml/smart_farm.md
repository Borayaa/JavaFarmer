# Smart Farm
You have fitted lines, drawn boundaries, grouped points, trained a drone by trial and error and built a neural network. Smart Farm puts all that Insight to work on the real farm.

## The bonus
Every level of Smart Farm makes all your plants grow 5% faster. The grow time of a plant is divided by `1 + 0.05 * level`:
- level `1`: plants need about 95% of their normal time
- level `3`: about 87%
- level `5`: 80%, so a field can be harvested 25% more often

The bonus works only while the AI / ML extension is enabled. Smart Farm has no builtins: once it is unlocked, it simply works.

## Cost
- Level `1` costs `1000000` Insight and `5000` gold.
- Level `2` costs `1500000` Insight and `7500` gold.
- Every further level costs 1.5 times as much as the one before, up to level `5`.

That is a lot of Insight, so it pays to look at where your Insight comes from. With the worked examples of these pages, these are the rates in Insight per `1000` ticks:
- [Machine Learning](docs/mods/ai_ml/machine_learning.md), the mean: about `38`
- [Linear Regression](docs/mods/ai_ml/linear_regression.md), the least squares line: about `58`
- [Classification](docs/mods/ai_ml/classification.md), the perceptron: about `75`
- [Clustering](docs/mods/ai_ml/clustering.md), k-means: about `50`
- [Reinforcement Learning](docs/mods/ai_ml/reinforcement_learning.md), Q-learning: about `80`
- [Neural Networks](docs/mods/ai_ml/neural_networks.md), the fine-tuned network: about `90`

Every one of these can be improved, some of them a lot. The exercises on each page are a good place to start.

## Several drones
Each puzzle has its own data: the regression datasets (shared by `submit_prediction()` and `submit_line()`), the crops and weeds, the clustering fields, the grid world and the XOR tasks. Two drones on the same puzzle would get in each other's way, because one drone's submission replaces the data the other one is still reading. Different puzzles do not disturb each other, so the natural plan is one drone per puzzle.

## Final challenge
Earn the Insight for Smart Farm level `1` as fast as you can.
- Run a different puzzle on every drone you have.
- Measure the Insight per `1000` ticks of each program with `get_tick_count()` and `num_items(Items.Insight)`, and improve the weakest one.
- Then try to get the neural network puzzle alone above `200` Insight per `1000` ticks. Almost all of the example's ticks go into reading points and fine-tuning.

<spoiler=show hint for the final challenge>Look at exercise 1 of [Neural Networks](docs/mods/ai_ml/neural_networks.md): in this puzzle a fresh design is cheaper than an epoch of fine-tuning. Also, you do not need all `40` points to find the middle and the diagonal of the square: how few can you use before the accuracy drops?</spoiler>
