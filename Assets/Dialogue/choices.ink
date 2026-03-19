VAR rounds = 0
VAR score = 0
VAR outcome = "" // Unity looks at this variable when the dialogue ends

-> main

=== main ===
{rounds < 3:
    Round {rounds + 1}: How will you approach the veteran?
    + [Show Military Respect]
        ~ score = score + 1
        -> chosen("showing respect")
    + [Insult His Service]
        ~ score = score - 1
        -> chosen("insulting him")
    + [Offer to Listen]
        ~ score = score + 1
        -> chosen("listening")
    + [Order him to stand down]
        -> chosen("being aggressive")
- else:
    // Final check after 3 rounds
    { score >= 2:
        "Alright... I trust you. Lead the way."
        ~ outcome = "follow"
    - else:
        "I knew you were just like the others. Get back!"
        ~ outcome = "fight"
    }
    -> END
}

=== chosen(choice) ===
~ rounds = rounds + 1
You chose {choice}.
-> main