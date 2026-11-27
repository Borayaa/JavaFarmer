// Editors can import the API stub: the imports have no effect in the game.
import "./farm.js";
import { canHarvest, harvest, Entities } from "./farm.js";
import * as farm from "./farm.js";

if (canHarvest()) {
	harvest();
}
farm.plant(Entities.Grass);
farm.move(farm.North);
