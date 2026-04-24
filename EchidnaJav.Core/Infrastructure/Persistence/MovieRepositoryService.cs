using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Domain.Entities;
using EchidnaJav.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace EchidnaJav.Core.Infrastructure.Persistence
{
    public interface IMovieRepositoryService
    {
        Task<MovieDetailsDto?> GetMovieDetailsAsync(string id);
        Task<List<MovieDto>> GetMoviesAsync(int skip, int take);
    }

    public class MovieRepositoryService : IMovieRepositoryService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IImageService _imageService;

        public MovieRepositoryService(
            IDbContextFactory<AppDbContext> dbFactory,
            IImageService imageService)
        {
            _dbFactory = dbFactory;
            _imageService = imageService;
        }

        public async Task<List<MovieDto>> GetMoviesAsync(int skip, int take)
        {
            using var db = _dbFactory.CreateDbContext();

            var movies = await db.Movies
                .AsNoTracking()
                .Include(m => m.Files)
                .OrderBy(m => m.Id)
                .Skip(skip)
                .Take(take)
                .Select(m => new MovieDto
                {
                    Id = m.Id,
                    Title = m.Title,
                    ImagePath = m.PrimaryImagePath
                })
                .ToListAsync();

            return movies;
        }

        public async Task<MovieDetailsDto?> GetMovieDetailsAsync(string id)
        {
            using var db = _dbFactory.CreateDbContext();

            return await db.Movies
                .AsNoTracking()
                .Where(m => m.Id == id)
                .Select(m => new MovieDetailsDto
                {
                    Id = m.Id,
                    Title = m.Title,
                    ImagePath = m.PrimaryImagePath,

                    Premiered = m.Premiered,
                    Runtime = m.Runtime,
                    Studio = m.Studio,
                    Director = m.Director,
                    Plot = m.Plot,

                    Cast = m.MovieActresses
                        .Select(a => a.Actress.Name)
                        .ToList(),

                    Genres = m.MovieGenres
                        .Select(g => g.Genre.Name)
                        .ToList(),

                    Files = m.Files.Select(f => new FileDto
                    {
                        FileName = f.FileName
                    }).ToList()
                })
                .FirstOrDefaultAsync();
        }
    }
}
