// ERROR: incompatible types: String cannot be converted to int
// AT: "one"
int one() {
    return "one";
}

void main() {
    one();
}
