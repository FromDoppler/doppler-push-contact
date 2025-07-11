using System.Runtime.CompilerServices;

// da permiso de los metodos internal del proyecto, al proyecto Doppler.PushContact.Test
[assembly: InternalsVisibleTo("Doppler.PushContact.Test")]
// da permiso a Castle DynamicProxy, que es el motor usado por Moq para crear los mocks de clases y métodos internos.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
