# ERROR: comparing with NULL using == does not work in R
# AT: ==
e <- get_entity_type()
if (e == NULL) plant(Entities$Grass)
