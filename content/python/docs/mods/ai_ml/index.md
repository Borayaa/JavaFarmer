# AI / ML
The farm produces data: water and yields, crops and weeds, spots that need a drone, a field full of ponds. With this extension your drone learns from that data, and every good model earns `Items.Insight`.
Each unlock is a small, honest version of a real machine learning method. You write the learning algorithm yourself, in the same language you use to farm, and the game scores your model on data it has not shown you.

## Insight
Insight is the currency of this extension. You earn it with the submission builtins (`submit_prediction()`, `submit_line()`, `submit_classifier()`, `evaluate_clusters()`, `evaluate_policy()` and `nn_evaluate()`) and spend it on the next unlocks.
Every reward follows the same pattern: the game measures the quality `q` of your model, a number from `0` to `1` where `1` is as good as the best known answer, and pays `base * q ** power` Insight, rounded down. The powers are high, so a model that is almost right pays much less than one that is right. Each page explains its own formula.
Check how much you have with `num_items(Items.Insight)`.

## The tech tree
Machine Learning costs `3000` pumpkins and `3000` carrots and appears once you have Utilities. Everything after it costs Insight.
- Machine Learning leads to Linear Regression (`20000` Insight) and to Clustering (`25000` Insight).
- Linear Regression leads to Classification (`60000` Insight), and Classification to Neural Networks (`150000` Insight).
- Clustering leads to Reinforcement Learning (`100000` Insight).
- Neural Networks leads to Smart Farm, the reward at the end. It has `5` levels and also costs gold: `1000000` Insight and `5000` gold for the first level.

## Unlocks
<unlock=machine_learning>- [Machine Learning](docs/mods/ai_ml/machine_learning.md): predict a single number; why the mean is the best guess.
</unlock><unlock=linear_regression>- [Linear Regression](docs/mods/ai_ml/linear_regression.md): fit a straight line by least squares, measured with R squared.
</unlock><unlock=classification>- [Classification](docs/mods/ai_ml/classification.md): tell crops from weeds with a perceptron, tested on hidden points.
</unlock><unlock=clustering>- [Clustering](docs/mods/ai_ml/clustering.md): place drone bases with k-means, without any labels.
</unlock><unlock=reinforcement_learning>- [Reinforcement Learning](docs/mods/ai_ml/reinforcement_learning.md): learn to cross a hidden field by trial and error with Q-learning.
</unlock><unlock=neural_networks>- [Neural Networks](docs/mods/ai_ml/neural_networks.md): solve XOR with a hidden layer and backpropagation.
</unlock><unlock=smart_farm>- [Smart Farm](docs/mods/ai_ml/smart_farm.md): all plants grow faster, 5% per level.
</unlock>
## General tips
- Learning costs ticks. Reading a value costs `1` tick and every calculation in your code costs ticks too, so a loop over the data that you run a hundred times is expensive. The submission builtins take `200` ticks each, as long as a drone action.
- Read the data into lists once and work on the lists.
- A submission scores your model and then loads new data at once. Compute everything you need before you submit.
- Use `get_tick_count()` and `num_items(Items.Insight)` to measure your Insight per `1000` ticks. That number tells you which improvement is worth it.
- Each puzzle has its own data, and all drones share it. Put each drone on a different puzzle; two drones on the same puzzle would replace each other's data.
- The game has no `exp` function: use `nn_sigmoid()` or `2.718281828 ** t`. A square root is `t ** 0.5`.

## Glossary
- Dataset: the data a model learns from. Each entry is a sample or point.
- Feature: an input of the model, like the water given to a plot.
- Label or target: the answer the model should predict.
- Model: a rule with adjustable numbers, the weights, that turns features into a prediction.
- Loss or error: a number that says how wrong a model is, for example the mean squared error.
- Training: adjusting the weights to make the loss small.
- Test set: data kept aside to check a trained model on points it has never seen.
- Overfitting: doing well on the training data but badly on new data.
- Epoch: one pass of training over all the training points.
- Learning rate: how big each training step is.
- Centroid: the center of a group of points.
- Policy: in reinforcement learning, the action to take in each state.
- Return: the total reward of one episode.
