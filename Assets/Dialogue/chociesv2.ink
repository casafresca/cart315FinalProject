VAR rounds = 0
VAR score = 0
VAR outcome = "" // Unity looks at this variable when the dialogue ends

-> main

=== main ===
{rounds < 3:
    "He stares at you across the room. What do you say?"
    + [Sir, I respect your service.]
        ~ score = score + 1
        -> chosen("showing military respect")
    + [Your service doesn't impress me.]
        ~ score = score - 1
        -> chosen("insulting his service")
    + [I'm here to listen if you want to talk.]
        ~ score = score + 1
        -> chosen("offering to listen")
    + [Stand down now, soldier.]
        -> chosen("ordering him to stand down")
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