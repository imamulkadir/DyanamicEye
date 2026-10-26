// WPF loads XAML through shared caches that are not safe when several test classes create windows on their own STA
// threads at the same time (seen as a random XamlParseException, 2026-10-26). Integration tests therefore run one class
// at a time; they take a few seconds in total.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
