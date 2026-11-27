# Linear Regression: Fitting a Line
The mean ignores the water completely. But in the farm's records, the yield clearly depends on it: more water, more yield (or, on some fields, less). A straight line can capture that:
`prediction = slope * x + intercept`
`submit_line(slope, intercept)` predicts `slope * sample_x(i) + intercept` for every sample of the current dataset. It uses the same data as [Machine Learning](docs/mods/ai_ml/machine_learning.md): `sample_count()`, `sample_x(i)` and `sample_y(i)`.

## Least squares
For every sample, the line makes an error `y - (slope * x + intercept)`, called the residual. The best line is the one with the smallest sum of squared residuals. This is the method of least squares, and the answer can be worked out in two steps.

Step 1, the intercept. Fix any slope. The question "which intercept is best" is exactly the question from the last page: which constant to add so that the squared errors are smallest. The answer is again a mean, so the best line always passes through the point of averages:
`intercept = mean_y - slope * mean_x`

Step 2, the slope. Measure everything from the point of averages: `dx = x - mean_x` and `dy = y - mean_y`. Now the line says `dy = slope * dx`, and changing the slope tilts it around the point of averages. Tilting helps some samples and hurts others. Each sample pulls on the slope with its residual times its `dx`, so samples far from the middle pull hardest. The slope is best where all the pulls cancel, when the sum of `dx * (dy - slope * dx)` is zero. Solving that gives
`slope = sum_dx_dy / sum_dx_dx`
where `sum_dx_dy` is the sum of all `dx * dy` and `sum_dx_dx` the sum of all `dx * dx`. The top is large and positive when plots with more water than average also yield more than average. It is negative when they yield less.

## R squared
`submit_line(slope, intercept)` returns R squared:
`r2 = 1 - squared_residuals / squared_deviations`
Here `squared_residuals` is the sum of the squared residuals of your line and `squared_deviations` is the same sum for the mean from the last page. So R squared measures how much better than the baseline your line is.
- `1` means the line goes through every point.
- `0` means it is no better than the mean.
- A negative value means it is worse than the mean.

Real data is noisy. On some fields the water barely matters and the noise is large, and then even the best possible line only reaches an R squared of `0.2`. That is not a mistake: it is the honest answer that water explains little of the yield there.
`submit_line()` takes `200` ticks and then loads the next dataset.

## Reward
Your line is compared with the best line on the same data: `q = r2 / best_r2`, and the reward is `40 * q ** 8` Insight, rounded down.
The power `8` is harsh: a line that is 1% worse than the best pays `36`, one that is 5% worse pays only `26`. Guessing by eye is not enough; computing the least squares line exactly pays the full `40` on every dataset, even on the noisy ones.
You can still submit the mean with `submit_prediction()` (`10` Insight), but a line is worth four times as much for the same `200` ticks.
This unlock has no levels.

## Worked example
Read the data into lists once, compute the two means, then the two sums, then the line.

`# Example: fit the least squares line to every dataset
while True:
	n = sample_count()
	xs = []
	ys = []
	for i in range(n):
		xs.append(sample_x(i))
		ys.append(sample_y(i))
	mean_x = 0
	mean_y = 0
	for i in range(n):
		mean_x = mean_x + xs[i]
		mean_y = mean_y + ys[i]
	mean_x = mean_x / n
	mean_y = mean_y / n
	top = 0
	bottom = 0
	for i in range(n):
		dx = xs[i] - mean_x
		top = top + dx * (ys[i] - mean_y)
		bottom = bottom + dx * dx
	slope = top / bottom
	intercept = mean_y - slope * mean_x
	r2 = submit_line(slope, intercept)
	quick_print("y =", slope, "* x +", intercept, "R2", r2)`

This pays about `58` Insight per `1000` ticks, roughly one and a half times the mean.

## In the real world
Linear regression is the workhorse of science and business: dose and effect in medicine, price and demand, a trend line through sales figures, the calibration of a sensor. It is also the building block of neural networks, where every neuron starts by computing a weighted sum plus a constant, which is a line in disguise.
A line shows that two things move together. It does not prove that one causes the other. On this farm the water really does cause the yield, because that is how the data was made. In real data you need an experiment to be sure.
Many machine learning models cannot be solved with a formula like this one, so they are trained by gradient descent: start with any line and nudge it downhill, step by step. Try it in exercise 1 and you will see why a formula is worth having when you can get one.

## Exercises
1. Find the line by gradient descent instead: start with `slope = 0` and `intercept = 0` and repeat many times: compute the average residual and the average of residual times `dx`, and move the intercept and the slope a small step in that direction. How many passes do you need to get the full `40` Insight, and how many ticks does that cost?
2. Save ticks: compute the four sums of `x`, `y`, `x * y` and `x * x` in a single loop, without storing lists. The slope is then `(n * sum_xy - sum_x * sum_y) / (n * sum_xx - sum_x * sum_x)`. Measure the Insight per `1000` ticks before and after.
3. Compute R squared yourself before submitting and compare it with what `submit_line()` returns.
4. Print the slope and R squared of many datasets. Which datasets have a low R squared, and why?
<spoiler=show hint for exercise 1>Work with `dx = x - mean_x` so that the slope and the intercept do not get in each other's way. With centered data, moving the slope by `0.05` times the average of residual times `dx` works well; the intercept is then simply `mean_y - slope * mean_x`. Every pass over the data costs ticks, which is the whole lesson.</spoiler>
<spoiler=show hint for exercise 4>When the slope is close to `0`, the water hardly changes the yield, and the noise is all that is left. R squared is low, and no line can do better.</spoiler>
