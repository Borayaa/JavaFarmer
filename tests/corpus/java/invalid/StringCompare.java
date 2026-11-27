// ERROR: bad operand types for binary operator '<'
// AT: <
void main() {
    String a = "x";
    if (a < "y") {
        harvest();
    }
}
