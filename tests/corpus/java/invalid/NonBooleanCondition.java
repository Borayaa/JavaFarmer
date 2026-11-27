// ERROR: incompatible types: int cannot be converted to boolean
// AT: getPosX()
void main() {
    if (getPosX()) {
        harvest();
    }
}
