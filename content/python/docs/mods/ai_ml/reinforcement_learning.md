# Reinforcement Learning: Learning by Doing
Somewhere on a small `5` by `5` field there is a patch of ripe crops, and somewhere there are three ponds. You do not get a map. All you can do is walk the drone around and see what happens, the way a child learns that a stove is hot.
Learning from rewards and punishments instead of from labelled examples is called reinforcement learning.

## The world
- The field has `env_state_count()` (`25`) cells. Cell `x`, `y` is the state `y * 5 + x`, so state `0` is the south-west corner, where every episode starts.
- There are `env_actions()` (`4`) actions: `0` North, `1` East, `2` South, `3` West. Walking into the fence keeps the drone where it is.
- Reaching the crops pays `20` and ends the episode. The crops are always at least `5` steps from the start.
- Falling into a pond costs `-20` and ends the episode.
- Every other step costs `-1`, so the drone should not dawdle. After `50` steps the episode ends anyway.
- The ground is muddy: with a chance of `0.1` the drone slips and takes a random action instead of yours.

## Playing
- `env_reset()` starts a new episode and returns the start state.
- `env_step(a)` takes action `a` and returns the reward of that step. Calling it when the episode is over is an error.
- `env_state()` is the state the drone is in now.
- `env_done()` is `True` once the episode is over.

All of them take only `1` tick, so practice is cheap. Your program pays for its own thinking, though.

## Policies and scoring
A policy says which action to take in every state. `submit_policy_action(state, a)` sets the action for one state (`1` tick). States you never set use action `0`.
`evaluate_policy()` runs your policy for `50` episodes and returns the average return, the total reward of an episode. It takes `200` ticks. Then a completely new world is made: the crops and ponds move and the policy is reset. Everything you learned is about the old world, so start learning from scratch.

## Q-learning
Let `q[s * 4 + a]` be your estimate of the Q-value: the return you expect if you take action `a` in state `s` and play well afterwards. If you knew the Q-values, the best policy would be easy: in every state take the action with the biggest one.
The Q-values are linked to each other. Taking action `a` in `s` earns a reward `r` and lands you in a state `s2`, from where the best you can do is the largest Q-value of `s2`. So a good estimate should be close to the target
`target = r + gamma * max(q[s2 * 4], q[s2 * 4 + 1], q[s2 * 4 + 2], q[s2 * 4 + 3])`
Q-learning nudges the estimate a little towards the target after every single step:
`q[s * 4 + a] = q[s * 4 + a] + alpha * (target - q[s * 4 + a])`
- `alpha`, the learning rate, is how far each step moves the estimate. Because the mud makes steps random, a single step proves little, so `alpha` is well below `1`.
- `gamma`, the discount, is how much a reward one step later is worth compared to one now. A value like `0.95` makes the drone prefer short paths.
- At the crops or in a pond the world really ends: there is no future, and the target is just `r`. At the `50` step limit it is different: the drone is still standing in a field with a future, so keep the `gamma` part. In this world a step that pays `-1` never ends anything by itself, so `r == -1` tells you to keep it.

## Exploring
If the drone always takes the action that looks best so far, it may never try the one that is actually best. Two simple remedies, used together in the example:
- Epsilon-greedy: with a small chance `epsilon`, take a random action instead of the best one.
- Optimistic start: set every Q-value to `10` at the beginning. Every action that has not been tried looks wonderful, so the drone tries them all, and the estimates drop to their real values as it learns.

## Reward
Your policy is compared with the best possible policy for this world (computed by the game with value iteration, which knows the map), on the same `50` episodes with the same slips. With `q = (score + 50) / (best + 50)` the reward is `8000 * q ** 4` Insight, rounded down.
A policy that never finishes scores `-50` and pays nothing. One that walks straight into a pond scores around `-22`, which pays only about `250`. A good policy usually pays more than `7000`.
This unlock has no levels.

## Worked example
Train for `150` episodes with Q-learning, submit the greedy policy, and do it all again in the next world.

`# Example: Q-learning, then submit the greedy policy
alpha = 0.3
gamma = 0.95
epsilon = 0.1
episodes = 150
while True:
	q = []
	for i in range(env_state_count() * env_actions()):
		q.append(10)
	for episode in range(episodes):
		s = env_reset()
		while not env_done():
			if random() < epsilon:
				a = min(3, (random() * 4) // 1)
			else:
				a = 0
				for b in range(1, 4):
					if q[s * 4 + b] > q[s * 4 + a]:
						a = b
			r = env_step(a)
			s2 = env_state()
			target = r
			if r == -1:
				target = r + gamma * max(q[s2 * 4], q[s2 * 4 + 1], q[s2 * 4 + 2], q[s2 * 4 + 3])
			q[s * 4 + a] = q[s * 4 + a] + alpha * (target - q[s * 4 + a])
			s = s2
	for s in range(env_state_count()):
		a = 0
		for b in range(1, 4):
			if q[s * 4 + b] > q[s * 4 + a]:
				a = b
		submit_policy_action(s, a)
	score = evaluate_policy()
	quick_print("average return", score)`

A world takes about `90000` ticks, around `70` Insight per `1000` ticks. In about one world out of twenty the training is not finished in time: the policy walks in circles or into a pond, and that world pays almost nothing. That is reinforcement learning: learning by trying is powerful, but it is also noisy.

## In the real world
Reinforcement learning taught computers to play Go, chess and Atari games better than people, keeps robots balanced and tunes the cooling of data centers. Deep Q-learning is the same update as yours, with a neural network instead of a table, because a real game has far too many states to list.
Its big weakness shows even here: it needs a lot of practice. That is why real systems mostly train in simulators, where failing is free, before they touch anything real.

## Exercises
1. Test before you submit: after training, run `20` episodes with the greedy policy yourself and count how often they reach the crops. If the answer is rarely, train more. Does that raise your Insight per `1000` ticks?
2. Tune `episodes`, `epsilon` and `alpha`. Measure the average Insight per `1000` ticks over at least `20` worlds for each setting.
3. Start the Q-values at `0` instead of `10`. How often does the drone end up preferring a pond to the long walk to the crops?
4. Learn the map instead of the values: walk around until you know where the crops and ponds are (a step reward of `20` or `-20` tells you), then compute the policy yourself by value iteration.
<spoiler=show hint for exercise 1>Use `env_reset()` and a loop of `env_step()` with the greedy action, and count the episodes where a step returned `20`. These test episodes cost ticks but no Insight.</spoiler>
<spoiler=show hint for exercise 4>After an `env_step()`, `env_state()` is the cell the drone landed on, so a reward of `20` or `-20` marks that cell. Once you know the map, repeat for every state: its value is the best over the actions of the reward plus `gamma` times the value of the next cell (count the slips too, or ignore them for a first version). About `30` rounds over all cells are enough on a `5` by `5` field.</spoiler>
