import 'package:flutter/material.dart';
import 'package:orvano_flutter/orvano_flutter.dart';

/// Shows the signed in user, or the sign up and sign in forms.
class Home extends StatefulWidget {
  /// Creates the home screen.
  const Home({super.key, required this.orvano});

  /// The app's Orvano client.
  final Orvano orvano;

  @override
  State<Home> createState() => _HomeState();
}

class _HomeState extends State<Home> {
  bool _loading = true;
  User? _user;

  @override
  void initState() {
    super.initState();
    _refresh();
  }

  // #region current-user
  /// Loads the signed in user, or null when nobody is signed in.
  Future<void> _refresh() async {
    User? user;
    if (await widget.orvano.client.getSession() != null) {
      try {
        user = await widget.orvano.account.get();
      } on OrvanoException catch (error) {
        if (error.status != 401) rethrow;
      }
    }
    if (!mounted) return;
    setState(() {
      _loading = false;
      _user = user;
    });
  }
  // #endregion current-user

  // #region actions
  /// Creates the user and signs them in; the client stores the session.
  Future<void> _signUp(String name, String email, String password) async {
    await widget.orvano.account.create(
      CreateAccountRequest(name: name, email: email, password: password),
    );
    await _refresh();
  }

  /// Signs an existing user in with their email and password.
  Future<void> _signIn(String email, String password) async {
    await widget.orvano.account.createPasswordSession(
      CreatePasswordSessionRequest(email: email, password: password),
    );
    await _refresh();
  }

  /// Ends this session on Orvano and forgets it on the device.
  Future<void> _signOut() async {
    await widget.orvano.account.deleteCurrentSession();
    await _refresh();
  }
  // #endregion actions

  @override
  Widget build(BuildContext context) {
    final user = _user;
    return Scaffold(
      appBar: AppBar(title: const Text('Orvano Flutter quickstart')),
      body: SafeArea(
        child: _loading
            ? const Center(child: CircularProgressIndicator())
            : ListView(
                padding: const EdgeInsets.all(24),
                children: user == null
                    ? [
                        AuthForm(
                          title: 'Sign up',
                          askName: true,
                          onSubmit: (name, email, password) =>
                              _signUp(name, email, password),
                        ),
                        const SizedBox(height: 32),
                        AuthForm(
                          title: 'Sign in',
                          onSubmit: (_, email, password) =>
                              _signIn(email, password),
                        ),
                      ]
                    : [
                        // #region signed-in
                        Text(
                          "You're signed in",
                          style: Theme.of(context).textTheme.headlineSmall,
                        ),
                        const SizedBox(height: 8),
                        Text('Name: ${user.name ?? 'No name'}'),
                        Text('Email: ${user.email ?? ''}'),
                        const SizedBox(height: 16),
                        FilledButton(
                          onPressed: _signOut,
                          child: const Text('Sign out'),
                        ),
                        // #endregion signed-in
                      ],
              ),
      ),
    );
  }
}

/// A form with email and password fields (and a name, for sign up) that shows
/// Orvano's message when the call fails, like a taken email or a wrong password.
class AuthForm extends StatefulWidget {
  /// Creates the form.
  const AuthForm({
    super.key,
    required this.title,
    required this.onSubmit,
    this.askName = false,
  });

  /// The heading and the button's label.
  final String title;

  /// Whether the form has a name field.
  final bool askName;

  /// Called with the name (empty without a name field), email, and password.
  final Future<void> Function(String name, String email, String password)
  onSubmit;

  @override
  State<AuthForm> createState() => _AuthFormState();
}

class _AuthFormState extends State<AuthForm> {
  final _name = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.onSubmit(_name.text, _email.text, _password.text);
    } on OrvanoException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text(widget.title, style: Theme.of(context).textTheme.titleLarge),
      if (widget.askName)
        TextField(
          key: Key('${widget.title} name'),
          controller: _name,
          decoration: const InputDecoration(labelText: 'Name'),
          autofillHints: const [AutofillHints.name],
        ),
      TextField(
        key: Key('${widget.title} email'),
        controller: _email,
        decoration: const InputDecoration(labelText: 'Email'),
        keyboardType: TextInputType.emailAddress,
        autofillHints: const [AutofillHints.email],
      ),
      TextField(
        key: Key('${widget.title} password'),
        controller: _password,
        decoration: const InputDecoration(labelText: 'Password'),
        obscureText: true,
      ),
      const SizedBox(height: 12),
      FilledButton(
        key: Key('${widget.title} button'),
        onPressed: _busy ? null : _submit,
        child: Text(widget.title),
      ),
      if (_error case final error?)
        Padding(
          padding: const EdgeInsets.only(top: 8),
          child: Text(
            error,
            style: TextStyle(color: Theme.of(context).colorScheme.error),
          ),
        ),
    ],
  );
}
