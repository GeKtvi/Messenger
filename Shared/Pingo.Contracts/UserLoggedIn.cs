namespace Pingo.Contracts;

// Published by Identity after every successful login. MassTransit identifies a message by its
// namespace and type name (urn:message:Pingo.Contracts:UserLoggedIn), so never rename or move it
// without a plan for messages already in flight. Add properties; don't remove or retype them.
public record UserLoggedIn(string Email);
