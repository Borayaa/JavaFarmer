// ERROR: break inside a switch case is only supported as the last statement of the case
// AT: break;
void main() {
    switch (getPosX()) {
        case 0:
            if (canHarvest()) {
                break;
            }
            harvest();
            break;
    }
}
