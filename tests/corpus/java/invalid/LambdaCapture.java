// ERROR: local variables referenced from a lambda expression must be final or effectively final
// AT: count
void main() {
    int count = 0;
    spawnDrone(() -> quickPrint(count));
    count++;
}
