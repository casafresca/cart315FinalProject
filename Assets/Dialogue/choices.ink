VAR rounds = 0

-> main

=== main ===
{rounds < 3:
    Please choose an option! (Round {rounds + 1})
    + [choice 1]
        -> chosen("choice 1")
    + [choice 2]
        -> chosen("choice 2")
    + [choice 3]
        -> chosen("choice 3")
    + [choice 4]
        -> chosen("choice 4")
- else:
    You have completed all rounds!
    -> END
}

=== chosen(choice) ===
~ rounds = rounds + 1
You chose {choice}!
-> main