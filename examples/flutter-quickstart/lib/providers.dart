import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:google_sign_in/google_sign_in.dart';
import 'package:orvano_flutter/orvano_flutter.dart';
import 'package:sign_in_with_apple/sign_in_with_apple.dart';

/// Google's web client ID, for native Google sign in:
/// `--dart-define=GOOGLE_WEB_CLIENT_ID=...`. On Android, Google puts it in the
/// ID token, so it must be the Client ID in the console's Google dialog.
const googleWebClientId = String.fromEnvironment('GOOGLE_WEB_CLIENT_ID');

/// Google's iOS client ID, for native Google sign in on iOS:
/// `--dart-define=GOOGLE_IOS_CLIENT_ID=...`. Add it under Native client IDs.
const googleIosClientId = String.fromEnvironment('GOOGLE_IOS_CLIENT_ID');

/// Where a provider sends the user back after redirect sign in: the app's own
/// scheme, its bundle ID and package name, registered as the project's iOS and
/// Android platforms.
final redirectUrl = Uri.parse('dev.orvano.quickstart://auth');

/// The four providers, each turned on in the console's Sign in methods page.
const providerNames = {
  OAuthProvider.google: 'Google',
  OAuthProvider.apple: 'Apple',
  OAuthProvider.github: 'GitHub',
  OAuthProvider.microsoft: 'Microsoft',
};

/// Whether this build signs in with providers: the redirect flow opens the
/// system's auth session and comes back on the app's scheme, which this
/// quickstart sets up on iOS and Android only.
bool get providersSupported =>
    !kIsWeb &&
    (defaultTargetPlatform == TargetPlatform.iOS ||
        defaultTargetPlatform == TargetPlatform.android);

bool get _onIos => defaultTargetPlatform == TargetPlatform.iOS;

// #region google
/// Native Google sign in: Google's own sheet, with no browser. A new nonce ties
/// the ID token to this sign in.
Future<void> signInWithGoogle(Orvano orvano) async {
  final nonce = OrvanoNonce.create();
  await GoogleSignIn.instance.initialize(
    clientId: _onIos ? _orNull(googleIosClientId) : null,
    serverClientId: _orNull(googleWebClientId),
    nonce: nonce.hashed,
  );
  final account = await GoogleSignIn.instance.authenticate();
  final idToken = account.authentication.idToken;
  if (idToken == null) throw StateError('Google returned no ID token.');
  await orvano.client.signInWithIdToken(
    provider: IdTokenProvider.google,
    idToken: idToken,
    nonce: nonce.raw,
  );
}
// #endregion google

// #region apple
/// Native Sign in with Apple on iOS. Apple sends the user's name only the first
/// time, so it goes along to Orvano.
Future<void> signInWithApple(Orvano orvano) async {
  final nonce = OrvanoNonce.create();
  final credential = await SignInWithApple.getAppleIDCredential(
    scopes: [
      AppleIDAuthorizationScopes.email,
      AppleIDAuthorizationScopes.fullName,
    ],
    nonce: nonce.hashed,
  );
  final idToken = credential.identityToken;
  if (idToken == null) throw StateError('Apple returned no ID token.');
  final name = [
    credential.givenName,
    credential.familyName,
  ].whereType<String>().join(' ');
  await orvano.client.signInWithIdToken(
    provider: IdTokenProvider.apple,
    idToken: idToken,
    nonce: nonce.raw,
    authorizationCode: credential.authorizationCode,
    name: name.isEmpty ? null : name,
  );
}
// #endregion apple

// #region redirect
/// Signs in by redirect: the system's auth session opens the provider and
/// closes when it sends the user back to [redirectUrl].
Future<void> signInByRedirect(Orvano orvano, OAuthProvider provider) async {
  await orvano.client.signInWithOAuth(provider, redirectUrl: redirectUrl);
}
// #endregion redirect

/// A button per provider. Google signs in natively on iOS and Android, Apple
/// natively on iOS, and the rest by redirect. With [link], each links the
/// provider to the signed in user by redirect instead.
class ProviderButtons extends StatefulWidget {
  /// Creates the buttons.
  const ProviderButtons({
    super.key,
    required this.orvano,
    required this.onDone,
    this.link = false,
    this.only,
  });

  /// The app's Orvano client.
  final Orvano orvano;

  /// Called after a sign in or link finished.
  final Future<void> Function() onDone;

  /// Whether the buttons link providers instead of signing in.
  final bool link;

  /// The providers to show; all four when null.
  final Iterable<OAuthProvider>? only;

  @override
  State<ProviderButtons> createState() => _ProviderButtonsState();
}

class _ProviderButtonsState extends State<ProviderButtons> {
  bool _busy = false;
  String? _error;

  Future<void> _go(OAuthProvider provider) async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final orvano = widget.orvano;
      if (widget.link) {
        await orvano.client.linkIdentity(provider, redirectUrl: redirectUrl);
      } else if (provider == OAuthProvider.google) {
        await signInWithGoogle(orvano);
      } else if (provider == OAuthProvider.apple && _onIos) {
        await signInWithApple(orvano);
      } else {
        await signInByRedirect(orvano, provider);
      }
      await widget.onDone();
    } on OrvanoException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } on Exception catch (error) {
      if (!_cancelled(error) && mounted) {
        setState(() => _error = 'Sign in didn\'t finish: $error');
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final only = widget.only?.toSet();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final MapEntry(key: provider, value: name)
            in providerNames.entries)
          if (only == null || only.contains(provider))
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: OutlinedButton(
                key: Key('${widget.link ? 'Link' : 'Sign in with'} $name'),
                onPressed: _busy ? null : () => _go(provider),
                child: Text(widget.link ? 'Link $name' : 'Sign in with $name'),
              ),
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
}

/// Whether the user closed the provider's sheet: nothing to show.
bool _cancelled(Exception error) => switch (error) {
  GoogleSignInException(code: GoogleSignInExceptionCode.canceled) => true,
  SignInWithAppleAuthorizationException(
    code: AuthorizationErrorCode.canceled,
  ) =>
    true,
  // flutter_web_auth_2, when the auth session is closed.
  PlatformException(code: 'CANCELED') => true,
  _ => false,
};

String? _orNull(String value) => value.isEmpty ? null : value;

/// Sign in with a provider, then see, link, and unlink providers. The app
/// `lib/main_providers.dart` starts on this screen.
class ProvidersScreen extends StatefulWidget {
  /// Creates the screen.
  const ProvidersScreen({super.key, required this.orvano});

  /// The app's Orvano client.
  final Orvano orvano;

  @override
  State<ProvidersScreen> createState() => _ProvidersScreenState();
}

class _ProvidersScreenState extends State<ProvidersScreen> {
  bool _loading = true;
  User? _user;
  List<Identity> _identities = const [];
  String? _error;

  @override
  void initState() {
    super.initState();
    _refresh();
  }

  /// Loads the signed in user and their providers, or nothing when signed out.
  Future<void> _refresh() async {
    User? user;
    var identities = const <Identity>[];
    if (await widget.orvano.client.getSession() != null) {
      try {
        user = await widget.orvano.account.get();
        identities = (await widget.orvano.account.listIdentities()).items;
      } on OrvanoException catch (error) {
        if (error.status != 401) rethrow;
      }
    }
    if (!mounted) return;
    setState(() {
      _loading = false;
      _user = user;
      _identities = identities;
      _error = null;
    });
  }

  /// Unlinks one provider; Orvano refuses the user's last way to sign in.
  Future<void> _unlink(Identity identity) async {
    try {
      await widget.orvano.account.deleteIdentity(identity.id);
      await _refresh();
    } on OrvanoException catch (error) {
      if (mounted) setState(() => _error = error.message);
    }
  }

  Future<void> _signOut() async {
    await widget.orvano.account.deleteCurrentSession();
    await _refresh();
  }

  @override
  Widget build(BuildContext context) {
    final user = _user;
    final linked = {for (final i in _identities) i.provider};
    final errorStyle = TextStyle(color: Theme.of(context).colorScheme.error);
    return Scaffold(
      appBar: AppBar(title: const Text('Sign in with a provider')),
      body: SafeArea(
        child: _loading
            ? const Center(child: CircularProgressIndicator())
            : ListView(
                padding: const EdgeInsets.all(24),
                children: [
                  if (!providersSupported)
                    const Text('Run this app on iOS or Android.')
                  else if (user == null)
                    ProviderButtons(orvano: widget.orvano, onDone: _refresh)
                  else ...[
                    Text(
                      "You're signed in",
                      style: Theme.of(context).textTheme.headlineSmall,
                    ),
                    const SizedBox(height: 8),
                    Text('Name: ${user.name ?? 'No name'}'),
                    Text('Email: ${user.email ?? 'No email'}'),
                    const SizedBox(height: 16),
                    FilledButton(
                      onPressed: _signOut,
                      child: const Text('Sign out'),
                    ),
                    const SizedBox(height: 32),
                    Text(
                      'Sign in methods',
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    if (_identities.isEmpty)
                      const Text('No providers linked yet.'),
                    for (final identity in _identities)
                      ListTile(
                        contentPadding: EdgeInsets.zero,
                        title: Text(
                          providerNames[identity.provider] ??
                              identity.provider.value,
                        ),
                        subtitle: Text(identity.email ?? 'No email'),
                        trailing: TextButton(
                          onPressed: () => _unlink(identity),
                          child: const Text('Unlink'),
                        ),
                      ),
                    if (_error case final error?)
                      Text(error, style: errorStyle),
                    ProviderButtons(
                      orvano: widget.orvano,
                      onDone: _refresh,
                      link: true,
                      only: providerNames.keys.where(
                        (p) => !linked.contains(p),
                      ),
                    ),
                  ],
                ],
              ),
      ),
    );
  }
}
