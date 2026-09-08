import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth';

/**
 * Giriş yapmamış kullanıcı korumalı bir rotaya (örn. /board) URL yazarak girmeye
 * çalışırsa /login sayfasına yönlendirir. Korumak istediğin her rotaya
 * `canActivate: [authGuard]` ekleyerek kullanabilirsin.
 */
export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isLoggedIn()) {
    return true;
  }

  router.navigate(['/login']);
  return false;
};
