// ERROR: ?: is only supported as a whole value here
// AT: getPosX() > 1 ? 1 : 2
void main() {
    int x = 3 + (getPosX() > 1 ? 1 : 2);
}
