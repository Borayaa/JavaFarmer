# Machine Learning: The Best Guess
The farm has kept records. Every dataset is a list of plots: how much water a plot was given and how much it yielded. Learning from records like these, instead of following rules someone wrote by hand, is what machine learning means.
Your first model is the simplest one there is: a single number. `submit_prediction(value)` predicts `value` as the yield of every plot in the dataset and tells you how wrong that was. Good guesses pay `Items.Insight`.

## Reading the data
- `sample_count()` is the number of samples in the current dataset (between `20` and `40`).
- `sample_x(i)` is the water given to plot `i` (from `0` to `10`).
- `sample_y(i)` is the yield of plot `i`. This is the number you try to predict.

Each of them takes `1` tick. Samples are numbered from `0` to `sample_count() - 1`.

## Measuring a mistake
If you predict `v` and the real yield is `y`, the error is `y - v`. Some errors are positive and some negative, so simply adding them up would let them cancel out. Instead we square every error and take the average:
`squared_error = (y - v) ** 2`
The average of these squares over all samples is the mean squared error (MSE). Squaring also makes big mistakes count much more than small ones: being off by `3` costs nine times as much as being off by `1`.
`submit_prediction(value)` returns the mean squared error of your guess. It takes `200` ticks and then loads the next dataset, so read everything you need before you submit.
`new_dataset()` throws the current dataset away without scoring it. It also takes `200` ticks and pays nothing.

## Which guess is best?
The best constant guess is the mean, the sum of all yields divided by their number. Here is why.
Imagine you start with some guess and slide it up a little. Every sample above the guess gets a smaller error, every sample below gets a bigger one. As long as the errors above outweigh the errors below, sliding up helps. You stop exactly where the errors balance: where the sum of all `y - v` is zero. That point is the sum of the yields divided by their number, the mean.
There is an even nicer way to see it. For any guess `v`:
`mse_of_v = mse_of_mean + (v - mean) ** 2`
The first part is the spread of the data itself (statisticians call it the variance), and no constant guess can get rid of it. The second part is the price of missing the mean, and it is zero only at the mean.

## Reward
Your quality is `q = mse_of_mean / mse_of_v`, a number from `0` to `1`. The reward is `10 * q ** 4` Insight, rounded down.
- The exact mean pays `10` Insight.
- Missing the mean by a third of the data's spread gives `q = 0.9`, which pays `6`.
- Missing by the full spread (one standard deviation) gives `q = 0.5`, which pays nothing.

So close is not good enough: compute the mean exactly. Every dataset is worth the same `10` Insight, so skipping one with `new_dataset()` never helps here.
This unlock has no levels.

## Worked example
Add up the yields, divide by the count, submit. The `while True` loop then starts on the next dataset.

`# Example: predict the mean yield of every dataset
while True:
	n = sample_count()
	total = 0
	for i in range(n):
		total = total + sample_y(i)
	mean = total / n
	error = submit_prediction(mean)
	quick_print("mean", mean, "mean squared error", error)`

One dataset costs about `260` ticks: `2` ticks per sample for reading and adding, plus `200` for the submission. That is around `38` Insight per `1000` ticks.

## In the real world
Predicting the mean is the baseline of data science. Before anyone trains a fancy model, they check what the plain average achieves. A model that cannot beat the baseline has learned nothing, and you would be surprised how often that happens.
The mean is also hiding inside much bigger models. Anything trained to minimize squared error, from a straight line to a huge neural network, is trying to predict the average outcome for each input.
Notice what this model ignores: the water. Plots that got more water may well yield more. Using `sample_x(i)` to make a better guess is the next step: [Linear Regression](docs/mods/ai_ml/linear_regression.md).

## Exercises
1. Submit `mean + 1` instead of the mean and print both the returned error and the Insight you got. Check the formula above: the error should be exactly `1` larger than for the mean.
2. Compute the variance yourself (the average of `(y - mean) ** 2`) before you submit the mean. It should equal the error that `submit_prediction()` returns.
3. Print the pairs `sample_x(i)` and `sample_y(i)` of one dataset. Do plots with more water yield more?
4. Use `get_tick_count()` and `num_items(Items.Insight)` to measure how much Insight you earn per `1000` ticks.
<spoiler=show hint for exercise 1>The error of `mean + 1` is `mse_of_mean + 1`. Remember to compute the mean and the variance before you submit, because `submit_prediction()` loads the next dataset.</spoiler>
<spoiler=show hint for exercise 4>Store `start = get_tick_count()` and `before = num_items(Items.Insight)` once at the top. After every submission, `(num_items(Items.Insight) - before) * 1000 / (get_tick_count() - start)` is your rate.</spoiler>
