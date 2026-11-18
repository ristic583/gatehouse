import { Button } from "@/components/ui/button";

export function SignOutButton() {
  return (
    <form action="/auth/logout" method="post">
      <Button type="submit" variant="outline">
        Sign out
      </Button>
    </form>
  );
}
