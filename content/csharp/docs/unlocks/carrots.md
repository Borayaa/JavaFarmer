# Carrots
Before you can plant carrots with `Plant(Entities.Carrot);`, you have to till the soil. This will change the ground to `Grounds.Soil`. To till the soil, simply call `Till();`. Calling `Till()` again will change it back to `Grounds.Grassland`.

`Till();
Plant(Entities.Carrot);`

Planting carrots costs wood and hay. These items will be automatically removed when calling `Plant(Entities.Carrot)`.

You can see the cost of any plant on it's [own page](objects/carrot).
