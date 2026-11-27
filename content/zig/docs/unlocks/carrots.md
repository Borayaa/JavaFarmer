# Carrots
Before you can plant carrots with `farm.plant(.Carrot)`, you have to till the soil. This will change the ground to `farm.Ground.Soil`. To till the soil, simply call `farm.till();`. Calling `farm.till()` again will change it back to `farm.Ground.Grassland`.

`farm.till();
_ = farm.plant(.Carrot);`

Planting carrots costs wood and hay. These items will be automatically removed when calling `farm.plant(.Carrot)`.

You can see the cost of any plant on it's [own page](objects/carrot).