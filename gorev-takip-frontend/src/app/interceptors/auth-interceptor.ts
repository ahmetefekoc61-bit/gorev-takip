import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth';

/**
 * Giden her isteğe token'ı ekler ve sunucudan 401 dönerse oturumu kapatıp kullanıcıyı
 * giriş ekranına yönlendirir.
 *
 * Önceden 401 hiç ele alınmıyordu: sekme açık bırakılıp token'ın süresi dolduğunda
 * kullanıcı uygulamada gezinmeye devam edebiliyor ama her ekranda "bir hata oluştu"
 * mesajı görüyordu; sorunun oturumun düşmesi olduğunu anlamasının bir yolu yoktu.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  const token = authService.getToken();
  const request = token
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      // Giriş/kayıt uçlarında 401 "e-posta veya şifre hatalı" demek; burada oturum
      // kapatmak yerine hatayı ekranın kendisine bırakıyoruz.
      const isAuthEndpoint = req.url.includes('/api/auth/');

      if (error.status === 401 && !isAuthEndpoint) {
        authService.logout();
        router.navigate(['/login'], { queryParams: { expired: '1' } });
      }

      return throwError(() => error);
    })
  );
};
