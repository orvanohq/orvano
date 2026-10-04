# Orvano Next.js quickstart

The finished app of the [Next.js quickstart](https://orvano.dev/docs/quickstarts/nextjs/): sign up, sign in, see your name and email, and sign out, with `@orvano/nextjs`, server actions, and the session in cookies.

To run it, start Orvano locally ([Run Orvano locally](https://orvano.dev/docs/local/)), add a Web platform `localhost` to your project, then:

```bash
cp .env.example .env.local   # then set NEXT_PUBLIC_ORVANO_PROJECT
npm install
npm run dev
```

Open http://localhost:3000.

## Sign in with a provider

The app also has a page for Google, Apple, GitHub, and Microsoft sign in at http://localhost:3000/providers, built on the route handler in `app/api/orvano/[...orvano]/route.ts`. Signed in, it lists your linked providers with **Unlink**, and a **Link** button for the others.

Turn each provider on first, in your project's **Sign in methods** page in the console: [Sign in with Google](https://orvano.dev/docs/auth/sign-in-with-google/) shows the setup, and the Apple, GitHub, and Microsoft pages work the same way. The providers send the browser back to Orvano's callback URL, so the app itself can stay on `localhost`.
