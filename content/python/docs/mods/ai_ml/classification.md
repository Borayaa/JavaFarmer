# Classification: Crops and Weeds
A new field has sprouted, and some of the sprouts are weeds. The farm has marked a few of them by hand: every training point has a position and a label, `1` for a crop and `-1` for a weed. Your job is to learn a rule that tells crops from weeds anywhere on the field.
Predicting a category instead of a number is called classification.

## Reading the data
- `point_count()` is the number of labelled training points (`40`).
- `point_x(i)` and `point_y(i)` are the position of point `i`, both between `0` and `10`.
- `point_label(i)` is its label: `1` (crop) or `-1` (weed).

Each takes `1` tick. On every field, crops and weeds are separated by a hidden straight line. But the farmhands were sloppy: about 3% of the training labels are wrong.

## A straight line as a rule
A linear classifier computes a score for every point and looks at its sign:
`is_crop = w_x * x + w_y * y + bias >= 0`
If `is_crop` is `True`, the point is called a crop, otherwise a weed. The points where the score is exactly `0` form a straight line, the decision boundary. The pair `w_x`, `w_y` points across the line towards the crop side, and `bias` moves the line.
`submit_classifier(w_x, w_y, bias)` uses this rule on `100` hidden test points that you never get to see. It returns the accuracy, the fraction of test points it got right, takes `200` ticks and loads the next field.

## Training and testing
Why hidden test points? Because remembering the training points is easy, and useless. A rule is only good if it also works on points it has never seen. This is called generalization, and every serious machine learning result is measured on data that was kept aside during training.
The test labels here are clean, but some training labels are wrong. A rule that bends over backwards to get every noisy training point right would be worse on the test set. That is overfitting. A straight line cannot bend, so a line that gets one or two training points wrong is often perfect on the test set.

## The perceptron
The perceptron is one of the oldest learning algorithms (Frank Rosenblatt, 1958) and it is still a beautiful one. Start with all weights at `0`. Go through the training points. Whenever a point is on the wrong side, which is when
`wrong = label * (w_x * x + w_y * y + bias) <= 0`
move the line towards it:
`w_x = w_x + label * x
w_y = w_y + label * y
bias = bias + label`
Each update increases the point's own `label * score` by `x * x + y * y + 1`, so it pushes the line in the right direction for that point. A full pass over the training points is called an epoch.
If a straight line can separate the training points with some room to spare, the perceptron is guaranteed to find one after a limited number of mistakes (this is the perceptron convergence theorem). With wrong labels in the data no such line exists, and the perceptron keeps jumping around forever. The fix is the pocket algorithm: after every epoch, count the training errors and keep the best weights seen so far "in your pocket".

## Centering the data
The hidden line passes near the middle of the field. If you use the raw positions, the bias has to become large while each update changes it by only `1`, so the perceptron needs many epochs. Subtract `5` from every `x` and `y` first, so that the field is centered on `0`. Afterwards, convert the bias back:
`bias = bias - 5 * w_x - 5 * w_y`

## Reward
Guessing at random already gets half of the points right, so only the part above that counts: `q = (accuracy - 0.5) / 0.5`. The reward is `800 * q ** 4` Insight, rounded down.
- accuracy `1` pays `800`
- accuracy `0.95` pays `524`
- accuracy `0.9` pays `327`
- accuracy `0.8` pays `103`

This unlock has no levels.

## Worked example
Read the field, run the perceptron for up to `12` epochs with a pocket, and stop early when an epoch makes no training errors.

`# Example: train a perceptron with a pocket on every field
epochs = 12
while True:
	n = point_count()
	xs = []
	ys = []
	labels = []
	for i in range(n):
		xs.append(point_x(i) - 5)
		ys.append(point_y(i) - 5)
		labels.append(point_label(i))
	w_x = 0
	w_y = 0
	bias = 0
	pocket = [0, 0, 0]
	pocket_errors = n + 1
	for epoch in range(epochs):
		for i in range(n):
			if labels[i] * (w_x * xs[i] + w_y * ys[i] + bias) <= 0:
				w_x = w_x + labels[i] * xs[i]
				w_y = w_y + labels[i] * ys[i]
				bias = bias + labels[i]
		errors = 0
		for i in range(n):
			if labels[i] * (w_x * xs[i] + w_y * ys[i] + bias) <= 0:
				errors = errors + 1
		if errors < pocket_errors:
			pocket_errors = errors
			pocket = [w_x, w_y, bias]
		if errors == 0:
			break
	w_x = pocket[0]
	w_y = pocket[1]
	bias = pocket[2] - 5 * w_x - 5 * w_y
	accuracy = submit_classifier(w_x, w_y, bias)
	quick_print("training errors", pocket_errors, "test accuracy", accuracy)`

A field takes about `10000` ticks, around `75` Insight per `1000` ticks.
There is no learning rate in this perceptron. Starting from zero, a learning rate would only scale all three weights by the same factor, and that does not move the line at all.

## In the real world
Linear classifiers are everywhere: spam filters, credit scoring, medical tests that combine a few measurements into a yes or no. Logistic regression and support vector machines are more refined ways to place the same kind of line.
The perceptron is also a single neuron: a weighted sum followed by a decision. Its weakness is that it can only draw straight lines, and some problems cannot be solved with one line. That is where [Neural Networks](docs/mods/ai_ml/neural_networks.md) begin.

## Exercises
1. Remove the pocket and submit the weights of the last epoch. Run both versions on many fields and compare the Insight. How much is the pocket worth?
2. Measure the ticks per field and the Insight per `1000` ticks. Try `epochs` from `4` to `30`. Where is the best trade-off?
3. The averaged perceptron: keep a running sum of the weights after every point and submit their average instead of the pocket. Averages are calmer than single snapshots. Does it generalize better?
4. A line that runs close to the training points is risky on the test set. Update not only when a point is wrong but also when it is barely right, when `label * score` is below a small margin. Does a margin help?
<spoiler=show hint for exercise 3>Keep `sum_x`, `sum_y` and `sum_b` and add the current `w_x`, `w_y` and `bias` to them after every training point, wrong or not. Only the direction of the line matters, so you can submit the sums directly without dividing.</spoiler>
<spoiler=show hint for exercise 4>Because the size of the weights grows during training, a fixed margin like `1` matters a lot at first and less later. Try values between `1` and `10` and compare the average Insight over at least `50` fields.</spoiler>
