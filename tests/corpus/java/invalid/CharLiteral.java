// ERROR: char literals are not supported in JavaFarmer
// AT: 'a'
void main() {
    String s = "abc";
    boolean b = s.charAt(0) == 'a';
}
