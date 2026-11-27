// ERROR: unreachable statement
// AT: harvest();
void main() {
    while (true) {
        break;
    }
    return;
    harvest();
}
