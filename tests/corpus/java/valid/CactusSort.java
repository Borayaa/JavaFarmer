// Sort a row of cacti with bubble sort using measure() and swap().
void sortRow() {
    int n = getWorldSize();
    boolean swapped = true;
    while (swapped) {
        swapped = false;
        for (int i = 0; i < n - 1; i++) {
            int here = (int) measure();
            int next = (int) measure(East);
            if (here > next) {
                swap(East);
                swapped = true;
            }
            move(East);
        }
        for (int i = 0; i < n - 1; i++) {
            move(West);
        }
    }
}

void main() {
    final int size = getWorldSize();
    int[] heights = new int[size];
    for (int x = 0; x < size; x++) {
        if (getGroundType() != Grounds.Soil) {
            till();
        }
        plant(Entities.Cactus);
        heights[x] = (int) measure();
        move(East);
    }
    var tallest = 0;
    for (int h : heights) {
        tallest = Math.max(tallest, h);
    }
    quickPrint("tallest", tallest);
    sortRow();
    harvest();
}
