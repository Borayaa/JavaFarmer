// switch (both forms), do-while, classic for with continue, ternary.
int score(Entity e) {
    switch (getGroundType() == Grounds.Soil ? 1 : 0) {
        case 1:
            return 2;
        default:
            return e == Entities.Grass ? 1 : 0;
    }
}

void main() {
    int moves = 0;
    do {
        move(East);
        moves++;
    } while (getPosX() != 0);

    for (int i = 0; i < moves; i++) {
        if (i % 2 == 1) {
            continue;
        }
        Entity e = getEntityType();
        String kind;
        switch (moves) {
            case 1 -> kind = "tiny";
            case 2, 3 -> kind = "small";
            default -> {
                kind = "big";
                quickPrint("big farm");
            }
        }
        quickPrint(kind, score(e));
    }
    int count = 0;
    for (int i = 10; i > 0; i -= 3) {
        count += i > 5 ? 2 : 1;
    }
    quickPrint(count);
}
