// The first program: harvest hay forever.
void main() {
    while (true) {
        if (canHarvest()) {
            harvest();
        }
    }
}
