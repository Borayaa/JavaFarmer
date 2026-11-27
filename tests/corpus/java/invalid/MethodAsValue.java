// ERROR: cannot find symbol: variable work (to pass a method use this::work)
// AT: work
void work() {
    harvest();
}

void main() {
    spawnDrone(work);
}
