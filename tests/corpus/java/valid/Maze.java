// Follow the treasure hints in a maze using measure().
Direction[] dirs = {North, East, South, West};

int turnRight(int d) {
    return (d + 1) % 4;
}

int turnLeft(int d) {
    return (d + 3) % 4;
}

void main() {
    plant(Entities.Bush);
    while (getEntityType() == Entities.Bush) {
        useItem(Items.Weird_Substance, getWorldSize());
    }
    int dir = 0;
    while (getEntityType() != Entities.Treasure) {
        int right = turnRight(dir);
        if (canMove(dirs[right])) {
            dir = right;
        } else if (!canMove(dirs[dir])) {
            dir = turnLeft(dir);
            continue;
        }
        move(dirs[dir]);
    }
    Object hint = measure();
    if (hint != null) {
        Position next = (Position) hint;
        quickPrint("next treasure", next.x(), next.y());
    }
    harvest();
}
