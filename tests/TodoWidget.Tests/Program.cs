using TodoWidget.Tests;

int failures = 0;
failures += CoreListTests.Suite().Run();
failures += CoreDockTests.Suite().Run();
failures += CoreHotkeyTests.Suite().Run();
failures += PersistenceTests.Suite().Run();
failures += UiLayoutTests.Suite().Run();
failures += DesktopPaletteTests.Suite().Run();

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : $"{failures} TEST(S) FAILED");
return failures == 0 ? 0 : 1;
