Write a deliberately bad ASP.NET Core 10 OrderController.cs for a legacy-code refactoring exercise.

Requirements:
- Approximately 300 lines.
- One giant POST /api/orders action.
- Mix business logic, EF Core data access, validation, and HTTP response shaping inline.
- Include four empty catch { } blocks that swallow exceptions.
- Use synchronous EF Core calls inside an async action.
- Return object instead of strongly typed responses.
- Include no tests.
- Include subtle realistic bugs, including an off-by-one error and a possible null dereference.
- Use poor separation of concerns and make the code difficult to test.
- Make it look like realistic legacy production code written several years ago.
- Do not refactor or improve the code.
- Output only the complete C# source for OrderController.cs.
