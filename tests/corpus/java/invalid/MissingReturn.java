// ERROR: missing return statement
// AT: }
int pick(boolean b) {
    if (b) {
        return 1;
    }
}

void main() {
    pick(true);
}
