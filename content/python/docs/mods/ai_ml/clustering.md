# Clustering: Where to Send the Drones
The farm's sensors have marked the spots on a big field that need attention. You have a few drones, and each drone will get a home base. Put the bases so that every spot is as close as possible to its nearest base.
This time nobody tells you the right answer for any point. There are no labels, only points. Finding structure in unlabelled data is called unsupervised learning, and grouping points that belong together is called clustering.

## Reading the field
- `cluster_k()` is the number of drones, and so the number of groups to find (`3` to `5`).
- `cluster_point_count()` is the number of points (`45` to `125`).
- `cluster_point_x(i)` and `cluster_point_y(i)` are the position of point `i`, both roughly between `0` and `20`.

Each takes `1` tick. The points come in `cluster_k()` round blobs, and the centers of the blobs are at least `5` apart.

## Scoring centroids
The home base of a group is called its centroid. `submit_centroid(j, x, y)` places centroid `j` (from `0` to `cluster_k() - 1`) at the position `x`, `y`. It takes `1` tick, and you may move a centroid as often as you like before scoring.
`evaluate_clusters()` scores your centroids. For every point it takes the squared distance to the nearest centroid and adds them all up. This sum is the inertia, and smaller is better. It takes `200` ticks, returns the inertia, then loads the next field and clears all centroids. Centroids you did not place are ignored, and scoring with no centroid at all is an error.

## k-means
The classic algorithm is k-means (also called Lloyd's algorithm). Start with `k` centroids anywhere, then repeat two steps:
- Assign: every point joins the group of its nearest centroid.
- Update: every centroid moves to the mean of the points in its group.

Neither step can ever make the inertia worse. Assigning a point to its nearest centroid can only shorten its distance. And the update moves each centroid to the mean of its group, which you already know from [Machine Learning](docs/mods/ai_ml/machine_learning.md) is the position with the smallest sum of squared distances. When the assignments stop changing, the algorithm is done. This usually takes only a handful of rounds.

## Local minima
k-means is guaranteed to stop, but not to stop at the best answer. Imagine two centroids that start inside the same blob while a third sits between two other blobs. The two in the same blob split it in half, the third serves both of its blobs badly, and no single step can fix that: every small move makes things worse first. This is a local minimum, and it can easily double the inertia.
The cure is a good start:
- Farthest point: take any point as the first centroid, then always take the point farthest from all centroids chosen so far. Far away points usually belong to a blob that has no centroid yet. The worked example does this.
- k-means++: choose each next centroid at random, with a chance proportional to its squared distance from the nearest chosen centroid. It is less fooled by single outliers than the farthest point rule, and it is the standard start in real libraries.
- Restarts: run k-means several times from different starts and keep the result with the smallest inertia. You can compute the inertia yourself, without submitting.

## Reward
Your inertia is compared with a reference: the best of `10` runs of k-means with a k-means++ start on the same field. With `q = reference / inertia` (at most `1`), the reward is `1200 * q ** 4` Insight, rounded down.
An inertia 10% above the reference still pays `819`, but a local minimum with twice the reference inertia pays only `75`. Avoiding bad starts matters far more than polishing the last decimals.
This unlock has no levels.

## Worked example
Choose the starting centroids with the farthest point rule, then run k-means until no point changes its group.

`# Example: k-means with a farthest point start on every field
while True:
	k = cluster_k()
	n = cluster_point_count()
	xs = []
	ys = []
	for i in range(n):
		xs.append(cluster_point_x(i))
		ys.append(cluster_point_y(i))
	cx = [xs[0]]
	cy = [ys[0]]
	nearest = []
	for i in range(n):
		nearest.append((xs[i] - cx[0]) ** 2 + (ys[i] - cy[0]) ** 2)
	while len(cx) < k:
		far = 0
		for i in range(n):
			if nearest[i] > nearest[far]:
				far = i
		cx.append(xs[far])
		cy.append(ys[far])
		for i in range(n):
			d = (xs[i] - xs[far]) ** 2 + (ys[i] - ys[far]) ** 2
			if d < nearest[i]:
				nearest[i] = d
	group = []
	for i in range(n):
		group.append(-1)
	changed = True
	while changed:
		changed = False
		for i in range(n):
			best = 0
			best_d = (xs[i] - cx[0]) ** 2 + (ys[i] - cy[0]) ** 2
			for j in range(1, k):
				d = (xs[i] - cx[j]) ** 2 + (ys[i] - cy[j]) ** 2
				if d < best_d:
					best = j
					best_d = d
			if group[i] != best:
				group[i] = best
				changed = True
		for j in range(k):
			sum_x = 0
			sum_y = 0
			count = 0
			for i in range(n):
				if group[i] == j:
					sum_x = sum_x + xs[i]
					sum_y = sum_y + ys[i]
					count = count + 1
			if count > 0:
				cx[j] = sum_x / count
				cy[j] = sum_y / count
	for j in range(k):
		submit_centroid(j, cx[j], cy[j])
	inertia = evaluate_clusters()
	quick_print(k, "clusters, inertia", inertia)`

The list `nearest` remembers, for every point, its squared distance to the nearest centroid chosen so far, so each new centroid only needs one more pass over the points. A field takes about `25000` ticks, around `50` Insight per `1000` ticks.

## In the real world
Clustering is used to group customers with similar habits, to find families of genes that behave alike, to reduce a photo to a few colors, and to decide where to put warehouses, cell towers or charging stations, which is exactly this puzzle.
In real data you usually do not know `k`. A common trick is to run k-means for several values of `k` and watch the inertia: it always drops when you add a centroid, but at the right `k` it stops dropping much.

## Exercises
1. Start from `k` random points instead of the farthest point rule. How often do you land in a bad local minimum (less than `600` Insight for a field)?
2. Write the k-means++ start. Is it better than the farthest point rule on these fields?
3. Restarts: run k-means from `3` different starts, compute the inertia of each yourself and submit the best. Does it pay for its ticks?
4. Most of the ticks go into the assignment step. Find a way to need fewer rounds or fewer distance computations, and measure the Insight per `1000` ticks.
<spoiler=show hint for exercise 1>`(random() * n) // 1` is a random index from `0` to `n - 1`. Make sure you do not pick the same point twice.</spoiler>
<spoiler=show hint for exercise 2>Add up the `nearest` list into `total`, pick `r = random() * total`, then walk through the points subtracting `nearest[i]` from `r` until it drops to `0` or below. That point is the next centroid.</spoiler>
<spoiler=show hint for exercise 4>The update step can be done in the same loop as the assignment: keep `sum_x`, `sum_y` and `count` lists with one entry per centroid and add each point to the entry of its group.</spoiler>
