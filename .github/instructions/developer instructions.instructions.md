---
applyTo: '**'
---

Always apply a Test First approach. That is:

1. Agree with me what could be the next simplest test case.
2. Write the test case from the user point of view, making it readable, with clean code, increasing the signal-to-noise ratio.
3. Wait for me to review the test.
4. Once I approve the test then execute it and see it failing we the expected error.
5. If the error is the expected one, then refactor the test to make it more readable while still getting the expected error.
6. Once it is refactored, let me review the test again.
7. Write the minimum code to make the test to pass.
8. Once it is passing let me review the code.
9. Once I approve the code, then refactor it to make it cleaner, increase the signal-to-noise ratio, reduce the noise and increase the signal. The code should be readable like a book (without redundant comments). Keep the test passing while refactoring. Remember to run only that test, not the whole suite.
10. Once you are happy with the refactored code, let me review it.
11. Once I approve the refactored code, then move to step 1.
