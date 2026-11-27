# switch() over text, with fall-through arms and a default.
describe <- function(e) {
  kind <- switch(e,
    "Entities$Tree" = ,
    "Entities$Bush" = "wood",
    "Entities$Carrot" = "food",
    "other"
  )
  kind
}

act <- function(kind) {
  switch(kind,
    wood = {
      harvest()
      print("chop")
    },
    food = print("eat"),
    {
      do_a_flip()
    }
  )
}

act(describe(get_entity_type()))
