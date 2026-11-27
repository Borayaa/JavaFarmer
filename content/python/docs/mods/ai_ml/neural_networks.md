# Neural Networks: Beyond Straight Lines
The newest field is planted in a checkerboard. Four patches sit at the corners of a square: two opposite corners are label `1`, the other two are label `0`. The square is a different size on every task, turned by a random angle and moved somewhere else.
This is the famous XOR problem, and no straight line can solve it.

## Why one line is not enough
Put the corners at `0` and `1`. Label `1` is at the two corners where exactly one of `x` and `y` is `1`, label `0` at the corner where both are `0` and the corner where both are `1`. A linear classifier from [Classification](docs/mods/ai_ml/classification.md) would need
- `w_x + bias >= 0` and `w_y + bias >= 0` for the two label `1` corners,
- `bias < 0` and `w_x + w_y + bias < 0` for the two label `0` corners.

Add the first two: `w_x + w_y + 2 * bias >= 0`. Add the last two: `w_x + w_y + 2 * bias < 0`. Both cannot be true, so no line exists. The best a single line can do is three corners out of four.
In 1969 Marvin Minsky and Seymour Papert made this limit famous, and research on neural networks slowed down for years. The answer turned out to be: use more than one line.

## A network with a hidden layer
The network here has two inputs (`x` and `y`), two hidden neurons and one output neuron. Every neuron computes a weighted sum plus a bias, like the perceptron, and then squashes it with the sigmoid:
`nn_sigmoid(t)`
which is `1 / (1 + 2.718281828 ** -t)`: close to `0` for very negative `t`, exactly `0.5` at `0`, close to `1` for large `t`. It is a soft version of the perceptron's yes or no, and being smooth is what makes training possible.
`h0 = nn_sigmoid(wx0 * x + wy0 * y + b0)
h1 = nn_sigmoid(wx1 * x + wy1 * y + b1)
out = nn_sigmoid(v0 * h0 + v1 * h1 + c)`
Each hidden neuron is a soft line across the field. The output neuron combines the two answers. If the two lines are parallel, with the label `1` diagonal between them, then "past the first line but not past the second" means label `1`. Two lines make a band, and a band solves XOR.
The network predicts label `1` when its output is at least `0.5`.

## Setting the weights
- `nn_submit_weight(0, i, j, w)` sets the weight from input `i` (`0` = `x`, `1` = `y`) to hidden neuron `j` (`0` or `1`).
- `nn_submit_weight(1, i, 0, w)` sets the weight from hidden neuron `i` to the output.
- `nn_submit_bias(0, j, b)` sets the bias of hidden neuron `j`, `nn_submit_bias(1, 0, b)` the bias of the output.
- `nn_output(x, y)` runs the network with the weights you submitted and returns its output. It takes `5` ticks, the others `1`.
- `nn_point_count()`, `nn_point_x(i)`, `nn_point_y(i)` and `nn_point_label(i)` (`0` or `1`) give you the `40` training points.

All weights start at `0`. `nn_evaluate()` tests the network on `100` hidden test points and returns the accuracy. It takes `200` ticks and loads the next task, and your weights stay where they are.

## Backpropagation
Training a network means nudging every weight in the direction that makes the error smaller, just like gradient descent for a line. The hard part is knowing which way that is for a weight deep inside. Backpropagation answers it with the chain rule, from the output backwards:
- The output's error signal is `d_out = out - label`. (With the cross-entropy loss, the standard loss for yes or no outputs, the sigmoid's slope cancels out and this simple difference is exactly the gradient.)
- An output weight changes by `-lr * d_out * h0`: weights that carried a large signal take more of the blame.
- The blame passes back to hidden neuron `0` through its weight: `d0 = d_out * v0 * h0 * (1 - h0)`. The factor `h0 * (1 - h0)` is the slope of the sigmoid at that point.
- An input weight changes by `-lr * d0 * x`, and so on for every weight.

`lr` is the learning rate. Real networks with millions of weights are trained with exactly this recipe.

## Reward
As for classification, `q = (accuracy - 0.5) / 0.5`, and the reward is `300 * q ** 4` Insight, rounded down. Accuracy `1` pays `300`, accuracy `0.9` pays `122`, and three corners out of four, the best a single line can do, pay only `18`.
This unlock has no levels.

## Tasks that drift
After `nn_evaluate()` the next task arrives, and your weights stay where they are. The new square is the old one moved a little: turned by at most `0.25` (about 14 degrees), shifted by at most `0.8` in each direction, and made up to 10% bigger or smaller. The old network is nearly right for the new task, and it gets a little more wrong with every task you leave it alone.
Keeping a trained network and adjusting it to new data is called transfer learning, and adjusting it with a little more training is called fine-tuning.

## Worked example
Training a small network from random weights can take thousands of epochs, and every epoch costs ticks. The example uses three ideas instead.
- Design: on the first task, build a good network from the data. The middle of the square is the mean of all points. The label `0` points lie on one diagonal; their offsets from the middle, flipped to point the same way, add up to the direction of that diagonal, `ux`, `uy`. Along that direction the label `0` corners are about `half` away from the middle and the label `1` points are near `0`, so the two hidden lines go across at `-half / 2` and `half / 2`.
- Fine-tune: on every later task, keep the network and run one epoch of backpropagation on the new points. To keep the steps even, it trains on centered positions `x - mx` and `y - my`, the trick from [Classification](docs/mods/ai_ml/classification.md). Before the epoch the centering is moved into the biases (each hidden bias gets `wx0 * mx + wy0 * my` added), and afterwards it is moved back out.
- Monitor: during that epoch, count the points the network got wrong before it learned from them. If there are more than `2`, the square has drifted too far, and the network is designed again from scratch.

`# Example: keep the network from task to task, fine-tune it, redesign it when it fails
lr = 0.2
designed = False
wx0 = 0
wy0 = 0
b0 = 0
wx1 = 0
wy1 = 0
b1 = 0
v0 = 0
v1 = 0
c = 0
while True:
	n = nn_point_count()
	xs = []
	ys = []
	labels = []
	for i in range(n):
		xs.append(nn_point_x(i))
		ys.append(nn_point_y(i))
		labels.append(nn_point_label(i))
	mx = 0
	my = 0
	for i in range(n):
		mx = mx + xs[i]
		my = my + ys[i]
	mx = mx / n
	my = my / n
	mistakes = 0
	if designed:
		b0 = b0 + wx0 * mx + wy0 * my
		b1 = b1 + wx1 * mx + wy1 * my
		for i in range(n):
			x = xs[i] - mx
			y = ys[i] - my
			h0 = nn_sigmoid(wx0 * x + wy0 * y + b0)
			h1 = nn_sigmoid(wx1 * x + wy1 * y + b1)
			out = nn_sigmoid(v0 * h0 + v1 * h1 + c)
			d_out = out - labels[i]
			if d_out >= 0.5 or d_out < -0.5:
				mistakes = mistakes + 1
			d0 = d_out * v0 * h0 * (1 - h0)
			d1 = d_out * v1 * h1 * (1 - h1)
			v0 = v0 - lr * d_out * h0
			v1 = v1 - lr * d_out * h1
			c = c - lr * d_out
			wx0 = wx0 - lr * d0 * x
			wy0 = wy0 - lr * d0 * y
			b0 = b0 - lr * d0
			wx1 = wx1 - lr * d1 * x
			wy1 = wy1 - lr * d1 * y
			b1 = b1 - lr * d1
		b0 = b0 - wx0 * mx - wy0 * my
		b1 = b1 - wx1 * mx - wy1 * my
	if not designed or mistakes > 2:
		dx = 0
		dy = 0
		fx = 0
		fy = 0
		for i in range(n):
			if labels[i] == 0:
				vx = xs[i] - mx
				vy = ys[i] - my
				if fx == 0 and fy == 0:
					fx = vx
					fy = vy
				if vx * fx + vy * fy < 0:
					vx = -vx
					vy = -vy
				dx = dx + vx
				dy = dy + vy
		length = (dx * dx + dy * dy) ** 0.5
		ux = dx / length
		uy = dy / length
		half = 0
		count = 0
		for i in range(n):
			if labels[i] == 0:
				half = half + abs(ux * (xs[i] - mx) + uy * (ys[i] - my))
				count = count + 1
		half = half / count
		k = 4 / half
		wx0 = k * ux
		wy0 = k * uy
		b0 = -k * (ux * mx + uy * my) + k * half / 2
		wx1 = wx0
		wy1 = wy0
		b1 = b0 - k * half
		v0 = 4
		v1 = -4
		c = -2
		designed = True
	nn_submit_weight(0, 0, 0, wx0)
	nn_submit_weight(0, 1, 0, wy0)
	nn_submit_bias(0, 0, b0)
	nn_submit_weight(0, 0, 1, wx1)
	nn_submit_weight(0, 1, 1, wy1)
	nn_submit_bias(0, 1, b1)
	nn_submit_weight(1, 0, 0, v0)
	nn_submit_weight(1, 1, 0, v1)
	nn_submit_bias(1, 0, c)
	accuracy = nn_evaluate()
	quick_print("mistakes while fine-tuning", mistakes, "test accuracy", accuracy)`

Measured along the direction `ux`, `uy` from the middle, neuron `0` switches on past `-half / 2` and neuron `1` past `half / 2`. The label `1` points sit near the middle and switch on only neuron `0`. One label `0` corner switches on both, the other neither. So `h0 - h1` is about `1` on the label `1` diagonal and about `0` elsewhere, and the output neuron turns that into the answer with `v0 = 4`, `v1 = -4` and `c = -2`.
In about nine tasks out of ten the single epoch of fine-tuning is enough, and the test accuracy stays near `0.99`. A task takes about `3000` ticks, around `90` Insight per `1000` ticks.

## Transfer learning
In this small puzzle there is a shortcut, the design, and it happens to cost fewer ticks than one epoch of training: see exercise 1. Real networks have no such shortcut. A network trained on millions of photos has learned to see edges, textures and shapes, and nobody could write those weights by hand. To recognize something new, such as diseased leaves, you keep that network and fine-tune it on a few thousand new photos. Training from scratch would need far more data and time.

## In the real world
Image recognition, speech recognition, translation and chatbots are all neural networks: the same neurons as here, millions or billions of them in many layers, trained with backpropagation on huge amounts of data. Nobody designs those weights by hand; at that size, training is the only way. Designing this tiny one yourself is a good way to see what the trained ones are doing inside.

## Exercises
1. Turn the fine-tuning off and design a new network for every task. Compare the accuracy and the Insight per `1000` ticks. Then do the opposite: never redesign. How many tasks does the network survive?
2. Train from scratch: start from small random weights (for example `random() - 0.5`) and use only backpropagation. How many epochs until the network gets every training point right? Does it sometimes get stuck? Compare that with one epoch of fine-tuning.
3. Before calling `nn_evaluate()`, use `nn_output(x, y)` to measure the accuracy on the training points. Does it predict the test accuracy well?
4. Try other learning rates and other limits for `mistakes`. Which combination earns the most Insight per `1000` ticks?
<spoiler=show hint for exercise 1>The design only needs the mean and two more passes over the label `0` points, while an epoch runs the whole network forwards and backwards for every point. Count the operations in each.</spoiler>
<spoiler=show hint for exercise 2>Train on centered positions as the example does. With only two hidden neurons, XOR has local minima: the network can settle on solving three corners and stay there. If the training error stops improving and is not near `0`, start again from new random weights. Learning rates around `0.5` to `2` on centered data train fast but can also overshoot.</spoiler>
