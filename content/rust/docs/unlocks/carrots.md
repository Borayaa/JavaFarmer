# Carrots
Before you can plant carrots with `plant(Entity::Carrot);`, you have to till the soil. This will change the ground to `Ground::Soil`. To till the soil, simply call `till();`. Calling `till()` again will change it back to `Ground::Grassland`.

`till();
plant(Entity::Carrot);`

Planting carrots costs wood and hay. These items will be automatically removed when calling `plant(Entity::Carrot)`.

You can see the cost of any plant on it's [own page](objects/carrot).
